using System.Text;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.EdgeNode;

public sealed partial class SparkplugEdgeNode
{
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
        if (parsed.MessageType is MessageTypes.NCmd && decoded.Find(RebirthMetric)?.Value is true)
        {
            LogRebirthRequested();
            _work.Writer.TryWrite(PublishBirthsAsync);
        }
        else if (parsed is { MessageType: MessageTypes.DCmd, DeviceId: { } device } && DeviceCommandReceived is { } handler)
        {
            _work.Writer.TryWrite(ct => handler(device, decoded, ct));
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
}
