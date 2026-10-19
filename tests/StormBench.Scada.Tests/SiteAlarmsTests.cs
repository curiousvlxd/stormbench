using StormBench.Application.Alarms;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Domain.Alarms;
using StormBench.Domain.Network;
using StormBench.Sparkplug.Payloads;

namespace StormBench.Scada.Tests;

public class SiteAlarmsTests
{
    private readonly SiteFixture _site = new();

    private Dictionary<string, bool> Conditions() =>
        SiteConditions.Evaluate(_site.Read()).ToDictionary(c => c.Definition.Id, c => c.Active);

    [Fact]
    public void Healthy_site_raises_nothing()
    {
        _site.PlcBirth(SparkplugMetric.Int64(PlcMetrics.Status, 0));
        _site.SimBirth(SparkplugMetric.Boolean(SimMetrics.OverflowActive("T1"), false));

        Assert.All(Conditions().Values, Assert.False);
    }

    [Theory]
    [InlineData(1, "PLC01.Fallback")]
    [InlineData(2, "PLC01.InputFault")]
    [InlineData(4, "PLC01.Clamped")]
    [InlineData(8, "PLC01.BadProposal")]
    public void Each_plc_status_bit_has_its_alarm(long bit, string id)
    {
        _site.PlcBirth(SparkplugMetric.Int64(PlcMetrics.Status, bit));

        var active = Conditions().Where(c => c.Value).Select(c => c.Key);
        Assert.Equal([id], active);
    }

    [Fact]
    public void Stale_plc_status_neither_raises_nor_clears()
    {
        _site.PlcBirth(SparkplugMetric.Int64(PlcMetrics.Status, 1));
        _site.PlcDeath();

        var conditions = Conditions();
        Assert.False(conditions.ContainsKey(AlarmDefinitions.Fallback.Id));
        Assert.True(conditions[AlarmDefinitions.PlcOffline.Id]);
    }

    [Fact]
    public void Edge_death_raises_the_gateway_alarm_not_a_second_plc_alarm()
    {
        _site.PlcBirth(SparkplugMetric.Int64(PlcMetrics.Status, 0));
        _site.EdgeDeath();

        var conditions = Conditions();
        Assert.True(conditions[AlarmDefinitions.EdgeOffline.Id]);
        Assert.False(conditions[AlarmDefinitions.PlcOffline.Id]);
    }

    [Fact]
    public void Lost_broker_raises_only_the_broker_alarm()
    {
        _site.PlcBirth(SparkplugMetric.Int64(PlcMetrics.Status, 1));
        Conditions();
        _site.BrokerConnected = false;

        var conditions = Conditions();
        Assert.Equal([AlarmDefinitions.Broker.Id], conditions.Keys);
        Assert.True(conditions[AlarmDefinitions.Broker.Id]);
    }

    [Fact]
    public void Not_yet_reaching_the_broker_after_a_start_is_no_alarm()
    {
        _site.BrokerConnected = false;

        Assert.Empty(Conditions());
        Assert.Equal(Presence.Unknown, _site.Read().Broker);

        _site.BrokerConnected = true;
        Assert.False(Conditions()[AlarmDefinitions.Broker.Id]);
        _site.BrokerConnected = false;
        Assert.Equal(Presence.Offline, _site.Read().Broker);
    }

    [Fact]
    public void Overflow_alarms_follow_the_simulated_field()
    {
        _site.SimBirth(
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("T1"), true),
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("CSO1"), false));

        var conditions = Conditions();
        Assert.True(conditions["T1.Overflow"]);
        Assert.False(conditions["CSO1.Overflow"]);
        Assert.False(conditions.ContainsKey("T2.Overflow"));
    }

    [Fact]
    public void Overflow_messages_name_tanks_and_structures()
    {
        Assert.Equal("Tank T1 overflowing", AlarmDefinitions.Overflow(new OverflowPointId("T1"), isTank: true).Message);
        Assert.Equal("CSO7 overflowing", AlarmDefinitions.Overflow(new OverflowPointId("CSO7"), isTank: false).Message);
        Assert.Equal(AlarmPriority.High, AlarmDefinitions.Overflow(new OverflowPointId("T1"), isTank: true).Priority);
    }

    [Fact]
    public void Simulator_death_raises_its_alarm_and_freezes_overflow_alarms()
    {
        _site.SimBirth(SparkplugMetric.Boolean(SimMetrics.OverflowActive("T1"), true));
        _site.SimDeath();

        var conditions = Conditions();
        Assert.True(conditions[AlarmDefinitions.SimOffline.Id]);
        Assert.False(conditions.ContainsKey("T1.Overflow"));
    }
}
