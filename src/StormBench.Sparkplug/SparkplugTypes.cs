using System.Globalization;
using System.Text.Json;

namespace StormBench.Sparkplug;

public static class MessageTypes
{
    public const string NBirth = "NBIRTH";
    public const string NDeath = "NDEATH";
    public const string DBirth = "DBIRTH";
    public const string DDeath = "DDEATH";
    public const string NData = "NDATA";
    public const string DData = "DDATA";
    public const string NCmd = "NCMD";
    public const string DCmd = "DCMD";
}

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
        if (parts.Length is < 4 or > 5 || parts[0] != Namespace || parts[1] == "STATE")
        {
            return false;
        }
        parsed = new SparkplugTopic(parts[1], parts[2], parts[3], parts.Length == 5 ? parts[4] : null);
        return true;
    }
}

#pragma warning disable CA1720 // Data type names follow the Sparkplug B specification.

/// <summary>Sparkplug B data types supported here (values as in sparkplug_b.proto).</summary>
public enum SparkplugDataType : uint
{
    Int64 = 4,
    UInt64 = 8,
    Double = 10,
    Boolean = 11,
    String = 12
}

/// <summary>A metric independent of the wire format. Value is long, ulong, double, bool, string or null.</summary>
public sealed record SparkplugMetric(string Name, SparkplugDataType DataType, object? Value, ulong? Timestamp = null)
{
    public static SparkplugMetric Int64(string name, long value) => new(name, SparkplugDataType.Int64, value);

    public static SparkplugMetric UInt64(string name, ulong value) => new(name, SparkplugDataType.UInt64, value);

    public static SparkplugMetric Double(string name, double value) => new(name, SparkplugDataType.Double, value);

    public static SparkplugMetric Boolean(string name, bool value) => new(name, SparkplugDataType.Boolean, value);

    public static SparkplugMetric String(string name, string value) => new(name, SparkplugDataType.String, value);
}

#pragma warning restore CA1720

/// <summary>A Sparkplug B payload independent of the wire format. Seq is absent in NDEATH and commands.</summary>
public sealed record SparkplugPayload(ulong Timestamp, ulong? Seq, IReadOnlyList<SparkplugMetric> Metrics)
{
    public SparkplugMetric? Find(string name) => Metrics.FirstOrDefault(m => m.Name == name);

    public static ulong Now(TimeProvider clock) => (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds();
}

/// <summary>Primary Host Application STATE (Sparkplug B 3.0): retained JSON on spBv1.0/STATE/{host id}.</summary>
public sealed record HostState(bool Online, long Timestamp)
{
    public string ToJson() =>
        $"{{\"online\":{(Online ? "true" : "false")},\"timestamp\":{Timestamp.ToString(CultureInfo.InvariantCulture)}}}";

    public static bool TryParse(string json, out HostState state)
    {
        state = new HostState(false, 0);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("online", out var online) || !root.TryGetProperty("timestamp", out var timestamp))
            {
                return false;
            }
            state = new HostState(online.GetBoolean(), timestamp.GetInt64());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
