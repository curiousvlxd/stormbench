using StormBench.Application.Alarms;
using StormBench.Contracts;
using StormBench.Sparkplug.Payloads;

namespace StormBench.Scada.Tests;

public class FieldChecksTests
{
    private readonly SiteFixture _site = new();
    private readonly FieldChecks _checks = new();
    private long _step;

    public FieldChecksTests()
    {
        _site.PlcBirth(SparkplugMetric.Double(PlcMetrics.Level("T1"), 1.0), SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), 0.3));
        _site.SimBirth(SparkplugMetric.Int64(SimMetrics.Run, 1), SparkplugMetric.Int64(SimMetrics.Step, 0));
        Evaluate();
    }

    private Dictionary<string, bool> Evaluate() => _checks.Evaluate(_site.Read()).ToDictionary(c => c.Definition.Id, c => c.Active);

    /// <summary>One step: the PLC's values, then the field's report of the same step.</summary>
    private Dictionary<string, bool> Step(double setpoint, double opening, double level)
    {
        _step++;
        _site.PlcData(
            SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), setpoint),
            SparkplugMetric.Double(PlcMetrics.Level("T1"), level),
            SparkplugMetric.Int64(PlcMetrics.StepAck, 1000 + _step));
        _site.SimData(
            SparkplugMetric.Int64(SimMetrics.Step, _step),
            SparkplugMetric.Int64(SimMetrics.PlcStep, 1000 + _step),
            SparkplugMetric.Double(SimMetrics.ValveOpening("V2"), opening));
        return Evaluate();
    }

    [Fact]
    public void Gate_that_does_not_follow_alarms_after_three_steps()
    {
        Assert.False(Step(1.0, 0.3, 1.1)["V2.GateDeviation"]);
        Assert.False(Step(1.0, 0.3, 1.2)["V2.GateDeviation"]);
        Assert.True(Step(1.0, 0.3, 1.3)["V2.GateDeviation"]);
        Assert.False(Step(1.0, 1.0, 1.4)["V2.GateDeviation"]);
    }

    [Fact]
    public void A_field_report_ahead_of_the_gateway_waits_for_the_plc_values_of_its_step()
    {
        var setpoints = new[] { 1.0, 0.24, 1.0, 0.24, 1.0, 0.24 };
        foreach (var setpoint in setpoints)
        {
            _step++;
            _site.SimData(
                SparkplugMetric.Int64(SimMetrics.Step, _step),
                SparkplugMetric.Int64(SimMetrics.PlcStep, 1000 + _step),
                SparkplugMetric.Double(SimMetrics.ValveOpening("V2"), setpoint));
            Assert.False(Evaluate()["V2.GateDeviation"]);
            _site.PlcData(SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), setpoint), SparkplugMetric.Int64(PlcMetrics.StepAck, 1000 + _step));
            Assert.False(Evaluate()["V2.GateDeviation"]);
        }
    }

    [Fact]
    public void Gate_stuck_while_the_setpoint_toggles_is_still_found()
    {
        Dictionary<string, bool> conditions = [];
        foreach (var setpoint in new[] { 1.0, 1.0, 0.24, 1.0, 1.0, 1.0 })
        {
            conditions = Step(setpoint, 0.24, 1.5);
        }
        Assert.True(conditions["V2.GateDeviation"]);
    }

    [Fact]
    public void Small_differences_and_a_moving_level_are_healthy()
    {
        for (var k = 0; k < 10; k++)
        {
            var conditions = Step(0.30, 0.32, 1.0 + (k * 0.1));
            Assert.False(conditions["V2.GateDeviation"]);
            Assert.False(conditions["T1.LevelFrozen"]);
        }
    }

    [Fact]
    public void Level_that_stops_moving_mid_tank_alarms_after_half_an_hour()
    {
        Dictionary<string, bool> conditions = [];
        for (var k = 0; k < FieldChecks.FrozenSteps + 1; k++)
        {
            conditions = Step(0.3, 0.3, 2.5);
        }
        Assert.True(conditions["T1.LevelFrozen"]);
        Assert.False(Step(0.3, 0.3, 2.6)["T1.LevelFrozen"]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    public void Empty_or_full_tanks_may_stay_put(double level)
    {
        Dictionary<string, bool> conditions = [];
        for (var k = 0; k < FieldChecks.FrozenSteps + 3; k++)
        {
            conditions = Step(0.3, 0.3, level);
        }
        Assert.False(conditions["T1.LevelFrozen"]);
    }

    [Fact]
    public void A_new_run_starts_counting_again()
    {
        Step(1.0, 0.3, 1.0);
        Step(1.0, 0.3, 1.1);
        _site.SimData(SparkplugMetric.Int64(SimMetrics.Run, 2), SparkplugMetric.Int64(SimMetrics.Step, 0));
        _step = 0;
        Evaluate();

        Assert.False(Step(1.0, 0.3, 1.2)["V2.GateDeviation"]);
    }
}
