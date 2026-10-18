namespace StormBench.Sparkplug.Tests;

public class SparkplugCodecTests
{
    [Fact]
    public void Round_trips_every_supported_type()
    {
        var payload = new SparkplugPayload(1_700_000_000_000, 42,
        [
            SparkplugMetric.Int64("Lockstep/StepReq", -7),
            SparkplugMetric.UInt64("bdSeq", ulong.MaxValue),
            SparkplugMetric.Double("Storage/T2/Level", 0.1 + 0.2),
            SparkplugMetric.Boolean("Node Control/Rebirth", true),
            SparkplugMetric.String("Properties/Name", "PLC01"),
            new SparkplugMetric("Outlets/V2/Setpoint", SparkplugDataType.Double, null),
        ]);

        var decoded = SparkplugCodec.Decode(SparkplugCodec.Encode(payload));

        Assert.Equal(payload.Timestamp, decoded.Timestamp);
        Assert.Equal(42UL, decoded.Seq);
        Assert.Equal(-7L, decoded.Find("Lockstep/StepReq")!.Value);
        Assert.Equal(ulong.MaxValue, decoded.Find("bdSeq")!.Value);
        Assert.Equal(0.1 + 0.2, decoded.Find("Storage/T2/Level")!.Value);
        Assert.Equal(true, decoded.Find("Node Control/Rebirth")!.Value);
        Assert.Equal("PLC01", decoded.Find("Properties/Name")!.Value);
        Assert.Null(decoded.Find("Outlets/V2/Setpoint")!.Value);
        Assert.All(decoded.Metrics, m => Assert.Equal(payload.Timestamp, m.Timestamp));
    }

    [Fact]
    public void Death_certificate_has_no_sequence_number()
    {
        var death = new SparkplugPayload(1, null, [SparkplugMetric.UInt64("bdSeq", 3)]);
        Assert.Null(SparkplugCodec.Decode(SparkplugCodec.Encode(death)).Seq);
    }

    [Fact]
    public void Nan_survives_the_wire()
    {
        var payload = new SparkplugPayload(1, 0, [SparkplugMetric.Double("x", double.NaN)]);
        Assert.True(double.IsNaN((double)SparkplugCodec.Decode(SparkplugCodec.Encode(payload)).Metrics[0].Value!));
    }
}
