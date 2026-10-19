using System.Globalization;
using System.Text.Json;

namespace StormBench.Sparkplug.Payloads;

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
