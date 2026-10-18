using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace StormBench.Sparkplug;

public sealed class EdgeNodeOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string GroupId { get; set; } = "";
    public string EdgeNodeId { get; set; } = "";

    /// <summary>Primary Host whose STATE gates the births (Sparkplug B 3.0); null publishes without waiting.</summary>
    public string? PrimaryHostId { get; set; }

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
}

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

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var worker = ProcessWorkAsync(cancellationToken);
        var session = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (session++ > 0)
                {
                    lock (_gate)
                    {
                        _bdSeq = (_bdSeq + 1) % 256;
                    }
                }
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
            try
            {
                await Task.Delay(_options.ReconnectDelay, _clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _work.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        _sessionEnded = NewSignal();
        lock (_gate)
        {
            _hostOnline = false;
            _hostOnlineSignal = NewSignal();
        }

        var death = SparkplugCodec.Encode(DeathPayload());
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId($"{_options.GroupId}-{_options.EdgeNodeId}")
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithSessionExpiryInterval(0)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(10))
            .WithWillTopic(NodeTopic(MessageTypes.NDeath).ToString())
            .WithWillPayload(death)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(false)
            .Build();
        var connected = await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        if (connected.ResultCode is not MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"broker refused the connection: {connected.ResultCode} {connected.ReasonString}");
        }
        LogConnected(_options.Host, _options.Port, BdSeq);

        var subscribe = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(NodeTopic(MessageTypes.NCmd).ToString(), MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(DeviceTopic(MessageTypes.DCmd, "+").ToString(), MqttQualityOfServiceLevel.AtLeastOnce);
        if (_options.PrimaryHostId is { } host)
        {
            subscribe.WithTopicFilter(SparkplugTopic.State(host), MqttQualityOfServiceLevel.AtLeastOnce);
        }
        await _client.SubscribeAsync(subscribe.Build(), cancellationToken).ConfigureAwait(false);

        if (_options.PrimaryHostId is null)
        {
            await PublishBirthsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Task hostOnline;
            lock (_gate)
            {
                hostOnline = _hostOnlineSignal.Task;
            }
            var first = await Task.WhenAny(hostOnline, _sessionEnded.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (first == hostOnline)
            {
                await PublishBirthsAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await _sessionEnded.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        LogSessionEnded(BdSeq);
    }

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
        if (!IsBorn || metrics.Count == 0)
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
                SparkplugMetric.Boolean(RebirthMetric, false),
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

    private void OnMessage(string topic, byte[] payload)
    {
        if (_options.PrimaryHostId is { } host && topic == SparkplugTopic.State(host))
        {
            if (HostState.TryParse(Encoding.UTF8.GetString(payload), out var state))
            {
                OnHostState(state);
            }
            return;
        }
        if (!SparkplugTopic.TryParse(topic, out var parsed))
        {
            return;
        }
        SparkplugPayload decoded;
        try
        {
            decoded = SparkplugCodec.Decode(payload);
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            LogBadPayload(topic);
            return;
        }
        if (parsed.MessageType == MessageTypes.NCmd && decoded.Find(RebirthMetric)?.Value is true)
        {
            LogRebirthRequested();
            _work.Writer.TryWrite(PublishBirthsAsync);
        }
        else if (parsed is { MessageType: MessageTypes.DCmd, DeviceId: { } device } && DeviceCommandReceived is { } handler)
        {
            _work.Writer.TryWrite(ct => handler(device, decoded, ct));
        }
    }

    private void OnHostState(HostState state)
    {
        bool goOffline;
        lock (_gate)
        {
            if (state.Timestamp < _hostTimestamp)
            {
                return;
            }
            _hostTimestamp = state.Timestamp;
            var wasOnline = _hostOnline;
            _hostOnline = state.Online;
            if (state.Online)
            {
                _hostOnlineSignal.TrySetResult();
            }
            goOffline = wasOnline && !state.Online;
        }
        if (goOffline)
        {
            LogHostOffline();
            _work.Writer.TryWrite(EndSessionAsync);
        }
    }

    /// <summary>The Primary Host went offline: publish the NDEATH and disconnect; the run loop starts a new session.</summary>
    private async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _born = false;
            }
            if (_client.IsConnected)
            {
                var death = new MqttApplicationMessageBuilder()
                    .WithTopic(NodeTopic(MessageTypes.NDeath).ToString())
                    .WithPayload(SparkplugCodec.Encode(DeathPayload()))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                await _client.PublishAsync(death, cancellationToken).ConfigureAwait(false);
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task ProcessWorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _work.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await item(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogWorkFailed(ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {Host}:{Port}, bdSeq {BdSeq}")]
    private partial void LogConnected(string host, int port, ulong bdSeq);

    [LoggerMessage(Level = LogLevel.Information, Message = "NBIRTH published, bdSeq {BdSeq}, {Devices} device(s)")]
    private partial void LogBorn(ulong bdSeq, int devices);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session with bdSeq {BdSeq} ended")]
    private partial void LogSessionEnded(ulong bdSeq);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT session failed: {Reason}")]
    private partial void LogSessionFailed(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebirth requested by NCMD")]
    private partial void LogRebirthRequested();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary host went offline: NDEATH and new session")]
    private partial void LogHostOffline();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Undecodable Sparkplug payload on {Topic}")]
    private partial void LogBadPayload(string topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Edge node work item failed: {Reason}")]
    private partial void LogWorkFailed(string reason);
}
