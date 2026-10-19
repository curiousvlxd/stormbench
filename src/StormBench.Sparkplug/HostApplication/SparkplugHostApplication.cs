using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.HostApplication;

/// <summary>
/// Sparkplug B 3.0 Primary Host Application on MQTTnet 5. It subscribes to its group before it
/// declares itself online, publishes STATE online (retained, QoS 1) with the same timestamp as
/// its will (STATE offline), hands every Sparkplug message to the consumer in arrival order,
/// and reports a lost broker connection so the consumer can mark its data stale.
/// </summary>
public sealed partial class SparkplugHostApplication : IAsyncDisposable
{
    private readonly HostApplicationOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly IMqttClient _client;
    private readonly Channel<(SparkplugTopic Topic, SparkplugPayload Payload)> _inbox =
        Channel.CreateUnbounded<(SparkplugTopic, SparkplugPayload)>(new UnboundedChannelOptions { SingleReader = true });
    private TaskCompletionSource _sessionEnded = NewSignal();
    private long _stateTimestamp;

    public SparkplugHostApplication(HostApplicationOptions options, TimeProvider clock, ILogger<SparkplugHostApplication> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _clock = clock;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            if (SparkplugTopic.TryParse(e.ApplicationMessage.Topic, out var topic))
            {
                try
                {
                    _inbox.Writer.TryWrite((topic, SparkplugCodec.Decode(e.ApplicationMessage.Payload.ToArray())));
                }
                catch (Google.Protobuf.InvalidProtocolBufferException)
                {
                    LogBadPayload(e.ApplicationMessage.Topic);
                }
            }
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += _ =>
        {
            _sessionEnded.TrySetResult();
            return Task.CompletedTask;
        };
    }

    /// <summary>Every Sparkplug message of the group, in arrival order.</summary>
    public Func<SparkplugTopic, SparkplugPayload, CancellationToken, Task>? MessageReceived { get; set; }

    /// <summary>The broker connection was lost: everything the host knows is stale now.</summary>
    public Action? ConnectionLost { get; set; }

    public bool IsConnected => _client.IsConnected;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var consumer = ConsumeAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or InvalidOperationException
                                           or TimeoutException or OperationCanceledException)
            {
                LogSessionFailed(ex.Message);
            }
            ConnectionLost?.Invoke();
            try
            {
                await Task.Delay(_options.ReconnectDelay, _clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _inbox.Writer.TryComplete();
        await consumer.ConfigureAwait(false);
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        _sessionEnded = NewSignal();
        _stateTimestamp = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var stateTopic = SparkplugTopic.State(_options.HostId);
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId($"host-{_options.HostId}")
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithSessionExpiryInterval(0)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(10))
            .WithWillTopic(stateTopic)
            .WithWillPayload(Encoding.UTF8.GetBytes(new HostState(false, _stateTimestamp).ToJson()))
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true)
            .Build();
        var connected = await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        if (connected.ResultCode is not MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"broker refused the connection: {connected.ResultCode} {connected.ReasonString}");
        }

        var subscribe = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(SparkplugTopic.GroupFilter(_options.GroupId), MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.SubscribeAsync(subscribe, cancellationToken).ConfigureAwait(false);
        await PublishStateAsync(new HostState(true, _stateTimestamp), cancellationToken).ConfigureAwait(false);
        LogOnline(_options.HostId, _options.Host, _options.Port);

        await _sessionEnded.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        LogSessionEnded();
    }

    private async Task PublishStateAsync(HostState state, CancellationToken cancellationToken) =>
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(SparkplugTopic.State(_options.HostId))
                .WithPayload(Encoding.UTF8.GetBytes(state.ToJson()))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(true)
                .Build(),
            cancellationToken).ConfigureAwait(false);

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var (topic, payload) in _inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (MessageReceived is not { } handler)
                {
                    continue;
                }
                try
                {
                    await handler(topic, payload, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogHandlerFailed(topic.ToString(), ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            try
            {
                await PublishStateAsync(new HostState(false, _stateTimestamp), CancellationToken.None).ConfigureAwait(false);
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or InvalidOperationException)
            {
                LogSessionFailed(ex.Message);
            }
        }
        _client.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
