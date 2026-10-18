using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Contracts;
using StormBench.Domain.Alarms;
using StormBench.Scada.Hmi;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>The overview display shows what the UNS says: PLC values for tanks and valves, field values for overflows.</summary>
public sealed class HmiFeedTests
{
    private readonly SiteFixture _site = new();
    private readonly AlarmEngine _alarms;
    private readonly HmiFeed _feed;

    public HmiFeedTests()
    {
        _alarms = new AlarmEngine(_site.Clock);
        _feed = new HmiFeed(_site.Reader, _alarms, new RunTrend(), _site);
    }

    private void Birth()
    {
        _site.PlcBirth(
            SparkplugMetric.Double(PlcMetrics.Level("T1"), 2.5),
            SparkplugMetric.Double(PlcMetrics.Level("T2"), 5.0),
            SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), 0.3),
            SparkplugMetric.Double(PlcMetrics.CenterSetpoint("V2"), 1.0),
            SparkplugMetric.Int64(PlcMetrics.ModeActive, (long)ControlMode.EfdCenter),
            SparkplugMetric.Int64(PlcMetrics.Status, 0));
        _site.SimBirth(
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("T2"), true),
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("CSO1"), false),
            SparkplugMetric.Double(SimMetrics.OverflowVolume("CSO1"), 36.0),
            SparkplugMetric.Double(SimMetrics.WwtpInflow, 1_000.0),
            SparkplugMetric.Double(SimMetrics.RainIntensity, 3.5));
    }

    [Fact]
    public void Nothing_to_draw_before_the_simulator_publishes_its_network()
    {
        Assert.Null(_feed.Build().Diagram);
    }

    [Fact]
    public void Tanks_and_valves_show_the_plc_values()
    {
        Birth();

        var view = _feed.Build();
        var diagram = view.Diagram!;
        var t1 = diagram.Tanks.Single(t => t.Shape.Id == "T1");
        var t2 = diagram.Tanks.Single(t => t.Shape.Id == "T2");
        var v2 = diagram.Valves.Single(v => v.Shape.Id == "V2");
        var v1 = diagram.Valves.Single(v => v.Shape.Id == "V1");

        Assert.Equal(0.5, t1.Fill);
        Assert.False(t1.Stale);
        Assert.False(t1.Overflowing);
        Assert.True(t2.Overflowing);
        Assert.Equal(0.3, v2.Setpoint);
        Assert.Equal(1.0, v2.Proposal);
        Assert.Null(v1.Setpoint);
        Assert.Equal(1_000.0, diagram.WwtpInflow);
        Assert.Equal(3.5, view.Rain);
        Assert.Equal(Domain.Control.ControlMode.EfdCenter, view.ModeActive);
        Assert.Equal(36.0, diagram.Weirs.Single().Volume);
    }

    [Fact]
    public void Values_of_a_lost_plc_stay_visible_but_stale()
    {
        Birth();
        _site.PlcDeath();

        var view = _feed.Build();

        var t1 = view.Diagram!.Tanks.Single(t => t.Shape.Id == "T1");
        Assert.True(t1.Stale);
        Assert.Equal(0.5, t1.Fill);
        Assert.Null(view.ModeActive);
    }

    [Fact]
    public void Overflow_alarm_is_attached_to_its_symbol()
    {
        Birth();
        _alarms.Apply(SiteConditions.Evaluate(_site.Read()));

        var view = _feed.Build();

        var t2 = view.Diagram!.Tanks.Single(t => t.Shape.Id == "T2");
        Assert.Equal("T2.Overflow", t2.Alarm?.Definition.Id);
        Assert.Null(view.Diagram.Tanks.Single(t => t.Shape.Id == "T1").Alarm);
        Assert.Equal(AlarmState.Unacknowledged, Assert.Single(view.Alarms).State);
    }

    [Fact]
    public void Version_changes_with_every_uns_message_and_alarm()
    {
        Birth();
        var before = _feed.Version;

        _site.SimData(SparkplugMetric.Double(SimMetrics.RainIntensity, 4.0));
        var afterMessage = _feed.Version;
        _alarms.Apply(SiteConditions.Evaluate(_site.Read()));

        Assert.NotEqual(before, afterMessage);
        Assert.NotEqual(afterMessage, _feed.Version);
    }
}
