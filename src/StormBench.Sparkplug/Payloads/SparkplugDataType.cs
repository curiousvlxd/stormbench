namespace StormBench.Sparkplug.Payloads;

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

#pragma warning restore CA1720
