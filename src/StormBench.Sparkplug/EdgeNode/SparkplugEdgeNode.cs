using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MQTTnet;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.EdgeNode;

/// <summary>
/// Sparkplug B 3.0 edge node on MQTTnet 5. One MQTT session per bdSeq: the NDEATH carrying the
/// session's bdSeq is registered as the will at connect time, the NBIRTH repeats it. Births wait
/// for the Primary Host to be online; if it goes offline, the node publishes its NDEATH,
/// disconnects and starts a new session. Every message after the NBIRTH carries seq + 1 mod 256.
/// Devices are published by exception: the node keeps their last metrics for the next birth.
/// </summary>
public sealed partial class SparkplugEdgeNode : IAsyncDisposable
{
    public const string RebirthMetric = "Node Control/Rebirth";
    public const string BdSeqMetric = "bdSeq";

    private readonly EdgeNodeOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, SparkplugMetric>> _devices = [];
    private readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>();

    private ulong _bdSeq;
    private byte _seq;
    private bool _born;
    private long _hostTimestamp = long.MinValue;
    private bool _hostOnline;
    private TaskCompletionSource _hostOnlineSignal = NewSignal();
    private TaskCompletionSource _sessionEnded = NewSignal();

    public SparkplugEdgeNode(EdgeNodeOptions options, TimeProvider clock, ILogger<SparkplugEdgeNode> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _clock = clock;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            OnMessage(e.ApplicationMessage.Topic, e.ApplicationMessage.Payload.ToArray());
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += _ =>
        {
            lock (_gate)
            {
                _born = false;
            }
            _sessionEnded.TrySetResult();
            return Task.CompletedTask;
        };
    }

    /// <summary>Called for every DCMD addressed to one of this node's devices.</summary>
    public Func<string, SparkplugPayload, CancellationToken, Task>? DeviceCommandReceived { get; set; }

    public bool IsBorn
    {
        get
        {
            lock (_gate)
            {
                return _born;
            }
        }
    }

    /// <summary>bdSeq of the current (or last) MQTT session.</summary>
    public ulong BdSeq
    {
        get
        {
            lock (_gate)
            {
                return _bdSeq;
            }
        }
    }

    private SparkplugTopic NodeTopic(string type) => new(_options.GroupId, type, _options.EdgeNodeId, null);

    private SparkplugTopic DeviceTopic(string type, string device) => new(_options.GroupId, type, _options.EdgeNodeId, device);

    /// <summary>Test hook and orderly shutdown helper: drop the connection so the broker sends the will (NDEATH).</summary>
    public Task DropConnectionAsync(CancellationToken cancellationToken) =>
        _client.DisconnectAsync(
            new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.DisconnectWithWillMessage).Build(),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            try
            {
                await EndSessionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or InvalidOperationException)
            {
                LogWorkFailed(ex.Message);
            }
        }
        _client.Dispose();
        _publishLock.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
