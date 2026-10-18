using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Formatter;

namespace StormBench.Sparkplug.Tests;

/// <summary>Edge node and primary host against a real broker: births, sequence, will, rebirth, STATE.</summary>
public sealed class SparkplugSessionTests(MosquittoFixture broker) : IClassFixture<MosquittoFixture>, IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly string _group = $"G{Guid.NewGuid():N}"[..12];
    private readonly ConcurrentQueue<(SparkplugTopic Topic, SparkplugPayload Payload)> _seen = new();
    private readonly CancellationTokenSource _stop = new();
    private IMqttClient _observer = null!;

    public async ValueTask InitializeAsync()
    {
        _observer = new MqttClientFactory().CreateMqttClient();
        _observer.ApplicationMessageReceivedAsync += e =>
        {
            if (SparkplugTopic.TryParse(e.ApplicationMessage.Topic, out var topic))
            {
                _seen.Enqueue((topic, SparkplugCodec.Decode(e.ApplicationMessage.Payload.ToArray())));
            }
            return Task.CompletedTask;
        };
        await _observer.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port).WithProtocolVersion(MqttProtocolVersion.V500).Build());
        await _observer.SubscribeAsync(SparkplugTopic.GroupFilter(_group));
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _observer.Dispose();
    }

    public void Dispose() => _stop.Dispose();

    private SparkplugEdgeNode Edge(string? primaryHost) => new(
        new EdgeNodeOptions
        {
            Host = broker.Host, Port = broker.Port, GroupId = _group, EdgeNodeId = "Edge01",
            PrimaryHostId = primaryHost, ReconnectDelay = TimeSpan.FromMilliseconds(200),
        },
        TimeProvider.System,
        NullLogger<SparkplugEdgeNode>.Instance);

    private SparkplugHostApplication Host(string hostId) => new(
        new HostApplicationOptions
        {
            Host = broker.Host, Port = broker.Port, HostId = hostId, GroupId = _group,
            ReconnectDelay = TimeSpan.FromMilliseconds(200),
        },
        TimeProvider.System,
        NullLogger<SparkplugHostApplication>.Instance);

    private async Task<(SparkplugTopic Topic, SparkplugPayload Payload)> WaitFor(string messageType, int occurrence = 1)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var matches = _seen.Where(m => m.Topic.MessageType == messageType).ToList();
            if (matches.Count >= occurrence)
            {
                return matches[occurrence - 1];
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"no {messageType} #{occurrence}; seen: {string.Join(", ", _seen.Select(m => m.Topic.MessageType))}");
    }

    private static ulong BdSeq(SparkplugPayload p) =>
        Convert.ToUInt64(p.Find(SparkplugEdgeNode.BdSeqMetric)!.Value!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Births_wait_for_the_primary_host_then_sequence_numbers_increase()
    {
        var hostId = $"H{Guid.NewGuid():N}"[..8];
        await using var edge = Edge(hostId);
        await edge.PublishDeviceDataAsync("PLC01", [SparkplugMetric.Double("Storage/T2/Level", 1.5)], CancellationToken.None);
        _ = edge.RunAsync(_stop.Token);

        await Task.Delay(700);
        Assert.DoesNotContain(_seen, m => m.Topic.MessageType == MessageTypes.NBirth);

        await using var host = Host(hostId);
        _ = host.RunAsync(_stop.Token);

        var nbirth = await WaitFor(MessageTypes.NBirth);
        var dbirth = await WaitFor(MessageTypes.DBirth);
        Assert.Equal(0UL, BdSeq(nbirth.Payload));
        Assert.Equal(0UL, nbirth.Payload.Seq);
        Assert.Equal(false, nbirth.Payload.Find(SparkplugEdgeNode.RebirthMetric)!.Value);
        Assert.Equal(1UL, dbirth.Payload.Seq);
        Assert.Equal(1.5, dbirth.Payload.Find("Storage/T2/Level")!.Value);

        await edge.PublishDeviceDataAsync("PLC01", [SparkplugMetric.Double("Storage/T2/Level", 1.75)], CancellationToken.None);
        var ddata = await WaitFor(MessageTypes.DData);
        Assert.Equal(2UL, ddata.Payload.Seq);
        Assert.Single(ddata.Payload.Metrics);
    }

    [Fact]
    public async Task Lost_connection_publishes_the_will_and_the_next_session_uses_the_next_bdseq()
    {
        await using var edge = Edge(primaryHost: null);
        _ = edge.RunAsync(_stop.Token);
        var first = await WaitFor(MessageTypes.NBirth);

        await edge.DropConnectionAsync(CancellationToken.None);

        var death = await WaitFor(MessageTypes.NDeath);
        Assert.Equal(BdSeq(first.Payload), BdSeq(death.Payload));
        Assert.Null(death.Payload.Seq);
        var second = await WaitFor(MessageTypes.NBirth, occurrence: 2);
        Assert.Equal(BdSeq(first.Payload) + 1, BdSeq(second.Payload));
    }

    [Fact]
    public async Task Rebirth_command_repeats_the_births_with_the_same_bdseq()
    {
        var hostId = $"H{Guid.NewGuid():N}"[..8];
        await using var edge = Edge(hostId);
        await edge.PublishDeviceDataAsync("PLC01", [SparkplugMetric.Int64("Lockstep/StepReq", 7)], CancellationToken.None);
        await using var host = Host(hostId);
        _ = host.RunAsync(_stop.Token);
        _ = edge.RunAsync(_stop.Token);
        var first = await WaitFor(MessageTypes.NBirth);
        await WaitFor(MessageTypes.DBirth);

        await host.RequestRebirthAsync(_group, "Edge01", CancellationToken.None);

        var second = await WaitFor(MessageTypes.NBirth, occurrence: 2);
        Assert.Equal(BdSeq(first.Payload), BdSeq(second.Payload));
        Assert.Equal(0UL, second.Payload.Seq);
        Assert.Equal(1UL, (await WaitFor(MessageTypes.DBirth, occurrence: 2)).Payload.Seq);
    }

    [Fact]
    public async Task Device_commands_reach_the_edge_node()
    {
        var hostId = $"H{Guid.NewGuid():N}"[..8];
        var received = new TaskCompletionSource<(string Device, SparkplugPayload Payload)>();
        await using var edge = Edge(hostId);
        edge.DeviceCommandReceived = (device, payload, _) =>
        {
            received.TrySetResult((device, payload));
            return Task.CompletedTask;
        };
        await using var host = Host(hostId);
        _ = host.RunAsync(_stop.Token);
        _ = edge.RunAsync(_stop.Token);
        await WaitFor(MessageTypes.NBirth);

        await host.SendDeviceCommandAsync(_group, "Edge01", "PLC01",
            [SparkplugMetric.Double("Outlets/V2/CenterSetpoint", 0.2366), SparkplugMetric.Int64("Coordination/Step", 12)],
            CancellationToken.None);

        var (device, command) = await received.Task.WaitAsync(Timeout);
        Assert.Equal("PLC01", device);
        Assert.Equal(0.2366, command.Find("Outlets/V2/CenterSetpoint")!.Value);
        Assert.Equal(12L, command.Find("Coordination/Step")!.Value);
    }

    [Fact]
    public async Task Primary_host_going_offline_ends_the_edge_session()
    {
        var hostId = $"H{Guid.NewGuid():N}"[..8];
        await using var edge = Edge(hostId);
        _ = edge.RunAsync(_stop.Token);
        var host = Host(hostId);
        _ = host.RunAsync(_stop.Token);
        var first = await WaitFor(MessageTypes.NBirth);

        await host.DisposeAsync();

        var death = await WaitFor(MessageTypes.NDeath);
        Assert.Equal(BdSeq(first.Payload), BdSeq(death.Payload));

        await using var again = Host(hostId);
        _ = again.RunAsync(_stop.Token);
        var second = await WaitFor(MessageTypes.NBirth, occurrence: 2);
        Assert.True(BdSeq(second.Payload) > BdSeq(first.Payload));
    }
}
