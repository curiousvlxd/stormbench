namespace StormBench.Sparkplug.Payloads;

#pragma warning disable CA1720 // Data type names follow the Sparkplug B specification.

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
