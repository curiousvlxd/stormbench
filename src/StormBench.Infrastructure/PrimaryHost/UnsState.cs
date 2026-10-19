using StormBench.Contracts;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Infrastructure.PrimaryHost;

public enum Quality
{
    Good,
    Stale
}

public sealed record MetricSnapshot(string Name, object? Value, Quality Quality, DateTimeOffset Updated);

public sealed record DeviceSnapshot(string DeviceId, bool Online, IReadOnlyList<MetricSnapshot> Metrics);

public sealed record NodeSnapshot(
    string GroupId,
    string EdgeNodeId,
    bool Online,
    ulong? BdSeq,
    IReadOnlyList<MetricSnapshot> Metrics,
    IReadOnlyList<DeviceSnapshot> Devices);

/// <summary>What the host should do after a message.</summary>
public enum HostAction
{
    None,

    /// <summary>The node's state is unknown or out of sequence: send NCMD Node Control/Rebirth.</summary>
    RequestRebirth
}

/// <summary>
/// Sparkplug B 3.0 Primary Host view of the edge nodes: births define the metrics, data updates
/// them, deaths and a lost broker connection mark them stale. The bdSeq of an NDEATH must match
/// the node's current NBIRTH, otherwise it belongs to an earlier session and is ignored. Every
/// message after NBIRTH must carry seq = previous + 1 (mod 256); a gap means messages were lost
/// and the node is asked to rebirth.
/// </summary>
public sealed class UnsState(TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Group, string Node), NodeEntry> _nodes = [];
    private long _version;

    /// <summary>Incremented by every change, so displays can skip rendering when nothing happened.</summary>
    public long Version => Interlocked.Read(ref _version);

    public HostAction Apply(SparkplugTopic topic, SparkplugPayload payload)
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _version);
            var key = (topic.GroupId, topic.EdgeNodeId);
            var now = clock.GetUtcNow();
            switch (topic.MessageType)
            {
                case MessageTypes.NBirth:
                    {
                        var node = new NodeEntry { Online = true, Seq = payload.Seq };
                        if (payload.Find(PlcMetrics.BdSeq)?.Value is { } bdSeq)
                        {
                            node.BdSeq = Convert.ToUInt64(bdSeq, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        Store(node.Metrics, payload, now);
                        _nodes[key] = node;
                        return HostAction.None;
                    }
                case MessageTypes.NDeath:
                    {
                        if (!_nodes.TryGetValue(key, out var node))
                        {
                            return HostAction.None;
                        }
                        var bdSeq = payload.Find(PlcMetrics.BdSeq)?.Value;
                        if (bdSeq is not null && node.BdSeq is not null
                            && Convert.ToUInt64(bdSeq, System.Globalization.CultureInfo.InvariantCulture) != node.BdSeq)
                        {
                            return HostAction.None;
                        }
                        node.MarkOffline(now);
                        return HostAction.None;
                    }
            }

            if (!_nodes.TryGetValue(key, out var current) || !current.Online)
            {
                return HostAction.RequestRebirth;
            }
            if (!current.NextSeqMatches(payload.Seq))
            {
                current.MarkOffline(now);
                return HostAction.RequestRebirth;
            }
            current.Seq = payload.Seq;

            switch (topic.MessageType)
            {
                case MessageTypes.NData:
                    Store(current.Metrics, payload, now);
                    break;
                case MessageTypes.DBirth when topic.DeviceId is not null:
                    {
                        var device = new DeviceEntry { Online = true };
                        Store(device.Metrics, payload, now);
                        current.Devices[topic.DeviceId] = device;
                        break;
                    }
                case MessageTypes.DData when topic.DeviceId is not null:
                    if (!current.Devices.TryGetValue(topic.DeviceId, out var target) || !target.Online)
                    {
                        return HostAction.RequestRebirth;
                    }
                    Store(target.Metrics, payload, now);
                    break;
                case MessageTypes.DDeath when topic.DeviceId is not null:
                    if (current.Devices.TryGetValue(topic.DeviceId, out var dead))
                    {
                        dead.MarkOffline(now);
                    }
                    break;
            }
            return HostAction.None;
        }
    }

    /// <summary>The host lost its broker connection: nothing it knows can be trusted any more.</summary>
    public void MarkAllStale()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _version);
            var now = clock.GetUtcNow();
            foreach (var node in _nodes.Values)
            {
                node.MarkOffline(now);
            }
        }
    }

    public bool TryGetDeviceMetric(string group, string node, string device, string metric, out MetricSnapshot value)
    {
        lock (_gate)
        {
            value = default!;
            if (_nodes.TryGetValue((group, node), out var n)
                && n.Devices.TryGetValue(device, out var d)
                && d.Metrics.TryGetValue(metric, out var m))
            {
                value = m;
                return true;
            }
            return false;
        }
    }

    public bool TryGetNodeMetric(string group, string node, string metric, out MetricSnapshot value)
    {
        lock (_gate)
        {
            value = default!;
            if (_nodes.TryGetValue((group, node), out var n) && n.Metrics.TryGetValue(metric, out var m))
            {
                value = m;
                return true;
            }
            return false;
        }
    }

    public IReadOnlyList<NodeSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _nodes.Select(kv => ToSnapshot(kv.Key, kv.Value)).ToList();
        }
    }

    /// <summary>One edge node with its devices, or false if it never sent a birth.</summary>
    public bool TryGetNode(string group, string node, out NodeSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue((group, node), out var entry))
            {
                snapshot = ToSnapshot((group, node), entry);
                return true;
            }
            snapshot = default!;
            return false;
        }
    }

    private static NodeSnapshot ToSnapshot((string Group, string Node) key, NodeEntry entry) => new(
        key.Group,
        key.Node,
        entry.Online,
        entry.BdSeq,
        [.. entry.Metrics.Values],
        [.. entry.Devices.Select(d => new DeviceSnapshot(d.Key, d.Value.Online, [.. d.Value.Metrics.Values]))]);

    private static void Store(Dictionary<string, MetricSnapshot> target, SparkplugPayload payload, DateTimeOffset now)
    {
        foreach (var metric in payload.Metrics)
        {
            target[metric.Name] = new MetricSnapshot(metric.Name, metric.Value, Quality.Good, now);
        }
    }

    private static void MarkStale(Dictionary<string, MetricSnapshot> metrics, DateTimeOffset now)
    {
        foreach (var name in metrics.Keys.ToList())
        {
            metrics[name] = metrics[name] with { Quality = Quality.Stale, Updated = now };
        }
    }

    private sealed class NodeEntry
    {
        public bool Online { get; set; }
        public ulong? BdSeq { get; set; }
        public ulong? Seq { get; set; }
        public Dictionary<string, MetricSnapshot> Metrics { get; } = [];
        public Dictionary<string, DeviceEntry> Devices { get; } = [];

        public bool NextSeqMatches(ulong? seq) => Seq is null || seq is null || seq == (Seq + 1) % 256;

        public void MarkOffline(DateTimeOffset now)
        {
            Online = false;
            MarkStale(Metrics, now);
            foreach (var device in Devices.Values)
            {
                device.MarkOffline(now);
            }
        }
    }

    private sealed class DeviceEntry
    {
        public bool Online { get; set; }
        public Dictionary<string, MetricSnapshot> Metrics { get; } = [];

        public void MarkOffline(DateTimeOffset now)
        {
            Online = false;
            MarkStale(Metrics, now);
        }
    }
}
