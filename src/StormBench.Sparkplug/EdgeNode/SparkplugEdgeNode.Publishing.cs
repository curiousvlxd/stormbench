using MQTTnet;
using MQTTnet.Protocol;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.EdgeNode;

public sealed partial class SparkplugEdgeNode
{
    /// <summary>Stores the device metrics and publishes the changed ones as DDATA when the node is born.</summary>
    public async Task PublishDeviceDataAsync(string deviceId, IReadOnlyCollection<SparkplugMetric> metrics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        bool knownDevice;
        lock (_gate)
        {
            knownDevice = _devices.TryGetValue(deviceId, out var store);
            store ??= _devices[deviceId] = [];
            foreach (var metric in metrics)
            {
                store[metric.Name] = metric;
            }
        }
        if (!IsBorn || metrics.Count is 0)
        {
            return;
        }
        await PublishAsync(knownDevice ? MessageTypes.DData : MessageTypes.DBirth, deviceId, knownDevice ? metrics : null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The device is unreachable: publish DDEATH and forget its metrics, so its next data is a new DBIRTH.</summary>
    public async Task PublishDeviceDeathAsync(string deviceId, CancellationToken cancellationToken)
    {
        bool known;
        lock (_gate)
        {
            known = _devices.Remove(deviceId);
        }
        if (!known)
        {
            return;
        }
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsBorn)
            {
                await SendAsync(DeviceTopic(MessageTypes.DDeath, deviceId), new SparkplugPayload(SparkplugPayload.Now(_clock), NextSeq(), []), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task PublishBirthsAsync(CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = SparkplugPayload.Now(_clock);
            ulong bdSeq;
            List<(string Device, SparkplugMetric[] Metrics)> devices;
            lock (_gate)
            {
                bdSeq = _bdSeq;
                _seq = 0;
                devices = _devices.Select(d => (d.Key, d.Value.Values.ToArray())).ToList();
            }
            var nbirth = new SparkplugPayload(now, 0,
            [
                SparkplugMetric.Int64(BdSeqMetric, (long)bdSeq),
                SparkplugMetric.Boolean(RebirthMetric, false)
            ]);
            await SendAsync(NodeTopic(MessageTypes.NBirth), nbirth, cancellationToken).ConfigureAwait(false);
            foreach (var (device, metrics) in devices)
            {
                await SendAsync(DeviceTopic(MessageTypes.DBirth, device), new SparkplugPayload(now, NextSeq(), metrics), cancellationToken)
                    .ConfigureAwait(false);
            }
            lock (_gate)
            {
                _born = true;
            }
            LogBorn(bdSeq, devices.Count);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task PublishAsync(string type, string deviceId, IReadOnlyCollection<SparkplugMetric>? metrics, CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsBorn)
            {
                return;
            }
            SparkplugMetric[] body;
            lock (_gate)
            {
                body = metrics?.ToArray() ?? _devices[deviceId].Values.ToArray();
            }
            await SendAsync(DeviceTopic(type, deviceId), new SparkplugPayload(SparkplugPayload.Now(_clock), NextSeq(), body), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private ulong NextSeq()
    {
        lock (_gate)
        {
            _seq = unchecked((byte)(_seq + 1));
            return _seq;
        }
    }

    private SparkplugPayload DeathPayload() =>
        new(SparkplugPayload.Now(_clock), null, [SparkplugMetric.Int64(BdSeqMetric, (long)BdSeq)]);

    private async Task SendAsync(SparkplugTopic topic, SparkplugPayload payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic.ToString())
            .WithPayload(SparkplugCodec.Encode(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .WithRetainFlag(false)
            .Build();
        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
