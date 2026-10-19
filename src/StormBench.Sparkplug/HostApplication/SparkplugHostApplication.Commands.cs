using MQTTnet;
using MQTTnet.Protocol;
using StormBench.Sparkplug.EdgeNode;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Sparkplug.HostApplication;

public sealed partial class SparkplugHostApplication
{
    public Task RequestRebirthAsync(string groupId, string edgeNodeId, CancellationToken cancellationToken) =>
        SendAsync(
            new SparkplugTopic(groupId, MessageTypes.NCmd, edgeNodeId, null),
            [SparkplugMetric.Boolean(SparkplugEdgeNode.RebirthMetric, true)],
            cancellationToken);

    public Task SendDeviceCommandAsync(
        string groupId, string edgeNodeId, string deviceId, IReadOnlyList<SparkplugMetric> metrics, CancellationToken cancellationToken) =>
        SendAsync(new SparkplugTopic(groupId, MessageTypes.DCmd, edgeNodeId, deviceId), metrics, cancellationToken);

    public Task SendNodeCommandAsync(
        string groupId, string edgeNodeId, IReadOnlyList<SparkplugMetric> metrics, CancellationToken cancellationToken) =>
        SendAsync(new SparkplugTopic(groupId, MessageTypes.NCmd, edgeNodeId, null), metrics, cancellationToken);

    private async Task SendAsync(SparkplugTopic topic, IReadOnlyList<SparkplugMetric> metrics, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            return;
        }
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic.ToString())
            .WithPayload(SparkplugCodec.Encode(new SparkplugPayload(SparkplugPayload.Now(_clock), null, metrics)))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build();
        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
