using StormBench.Sparkplug;

namespace StormBench.Contracts.Tests;

public class UnsTests
{
    [Fact]
    public void Builds_sparkplug_topics()
    {
        Assert.Equal("spBv1.0/Astlingen/NBIRTH/Edge01", Uns.NodeTopic(MessageTypes.NBirth));
        Assert.Equal("spBv1.0/Astlingen/DDATA/Edge01/PLC01", Uns.DeviceTopic(MessageTypes.DData));
        Assert.Equal("spBv1.0/STATE/Scada01", Uns.StateTopic());
    }

    [Theory]
    [InlineData("spBv1.0/Astlingen/DDATA/Edge01/PLC01", "DDATA", "PLC01")]
    [InlineData("spBv1.0/Astlingen/NDEATH/Edge01", "NDEATH", null)]
    public void Parses_sparkplug_topics(string topic, string messageType, string? device)
    {
        Assert.True(SparkplugTopic.TryParse(topic, out var parsed));
        Assert.Equal("Astlingen", parsed.GroupId);
        Assert.Equal(messageType, parsed.MessageType);
        Assert.Equal("Edge01", parsed.EdgeNodeId);
        Assert.Equal(device, parsed.DeviceId);
    }

    [Theory]
    [InlineData("spBv1.0/STATE/Scada01")]
    [InlineData("other/Astlingen/DDATA/Edge01")]
    [InlineData("spBv1.0/Astlingen")]
    public void Rejects_other_topics(string topic) => Assert.False(SparkplugTopic.TryParse(topic, out _));

    [Fact]
    public void Controlled_tanks_follow_station_order()
    {
        Assert.Equal(PlcMetrics.ControlledValves.Count, PlcMetrics.ControlledTanks.Count);
        Assert.Equal("Storage/T2/Level", PlcMetrics.Level(PlcMetrics.ControlledTanks[0]));
        Assert.Equal("Outlets/V6/CenterSetpoint", PlcMetrics.CenterSetpoint(PlcMetrics.ControlledValves[3]));
    }
}
