namespace StormBench.Sparkplug.Payloads;

/// <summary>A Sparkplug B payload independent of the wire format. Seq is absent in NDEATH and commands.</summary>
public sealed record SparkplugPayload(ulong Timestamp, ulong? Seq, IReadOnlyList<SparkplugMetric> Metrics)
{
    public SparkplugMetric? Find(string name) => Metrics.FirstOrDefault(m => m.Name == name);

    public static ulong Now(TimeProvider clock) => (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds();
}
