namespace StormBench.Sparkplug.Topics;

/// <summary>spBv1.0/{group}/{message type}/{edge node}[/{device}]; STATE topics are handled separately.</summary>
public readonly record struct SparkplugTopic(string GroupId, string MessageType, string EdgeNodeId, string? DeviceId)
{
    public const string Namespace = "spBv1.0";

    public override string ToString() =>
        DeviceId is null
            ? $"{Namespace}/{GroupId}/{MessageType}/{EdgeNodeId}"
            : $"{Namespace}/{GroupId}/{MessageType}/{EdgeNodeId}/{DeviceId}";

    public static string State(string hostId) => $"{Namespace}/STATE/{hostId}";

    public static string GroupFilter(string groupId) => $"{Namespace}/{groupId}/#";

    public static bool TryParse(string topic, out SparkplugTopic parsed)
    {
        parsed = default;
        var parts = topic.Split('/');
        if (parts.Length is < 4 or > 5 || parts[0] is not Namespace || parts[1] is "STATE")
        {
            return false;
        }
        parsed = new SparkplugTopic(parts[1], parts[2], parts[3], parts.Length is 5 ? parts[4] : null);
        return true;
    }
}
