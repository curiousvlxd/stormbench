using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.EdgeNode;

public sealed partial class SparkplugEdgeNode
{
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
}
