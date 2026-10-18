using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Runs;
using StormBench.Contracts;
using StormBench.Domain.Runs;
using StormBench.Infrastructure.VictoriaMetrics;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>What the historian writes for each control step: in simulated time, with the run's labels.</summary>
public class RunSamplerTests
{
    private readonly SiteFixture _site = new();
    private readonly MemorySampleSink _sink = new();
    private readonly RunRecorder _runs;
    private readonly AlarmEngine _alarms;
    private readonly RunSampler _sampler;

    public RunSamplerTests()
    {
        _runs = new RunRecorder(new MemoryRunStore(), _site.Clock);
        _alarms = new AlarmEngine(_site.Clock);
        _sampler = new RunSampler(_runs, _alarms, new PrometheusRunSeries(_sink));
        _site.SimBirth();
        _site.PlcBirth(
            SparkplugMetric.Double(PlcMetrics.Level("T1"), 0.5),
            SparkplugMetric.Double(PlcMetrics.Level("T2"), 1.25),
            SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), 0.3),
            SparkplugMetric.Int64(PlcMetrics.ModeActive, 3));
    }

    /// <summary>The worker's order: run history, alarms, then the samples.</summary>
    private void Do(Action<SiteFixture> message)
    {
        message(_site);
        var snapshot = _site.Read();
        _runs.Observe(snapshot);
        _alarms.Apply(SiteConditions.Evaluate(snapshot), snapshot.SimClock);
        _sampler.Observe(snapshot);
    }

    private string Run => _runs.Current!.Id.Value;

    [Fact]
    public void Every_step_is_written_at_its_simulated_time_with_the_run_labels()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 0, 0, rain: 2.5));
        Do(s => s.Step(2, 40, 50));

        Assert.Equal(2, _sink.Batches.Count);
        var labels = $"run=\"{Run}\",storm=\"s1\",algorithm=\"efd-center\"";
        var step2 = _sink.Batches[1].ToDictionary(s => s.Series);
        var ms = SiteFixture.ClockMs(RunScript.At(2));
        Assert.All(step2.Values, s => Assert.Equal(ms, s.TimestampMs));
        Assert.Equal(2, step2[$"stormbench_step{{{labels}}}"].Value);
        Assert.Equal(40, step2[$"stormbench_overflow_total_cubic_meters{{{labels}}}"].Value);
        Assert.Equal(50, step2[$"stormbench_basecase_overflow_total_cubic_meters{{{labels}}}"].Value);
        Assert.Equal(40, step2[$"stormbench_overflow_volume_cubic_meters{{{labels},point=\"CSO1\"}}"].Value);
        Assert.Equal(200, step2[$"stormbench_wwtp_inflow_cubic_meters{{{labels}}}"].Value);
        Assert.Equal(1.25, step2[$"stormbench_tank_level_meters{{{labels},tank=\"T2\"}}"].Value);
        Assert.Equal(0.3, step2[$"stormbench_valve_setpoint_ratio{{{labels},valve=\"V2\"}}"].Value);
        Assert.Equal(3, step2[$"stormbench_plc_mode{{{labels}}}"].Value);
        Assert.Equal(0, step2[$"stormbench_shadow_mismatches{{{labels}}}"].Value);
        Assert.Equal(2.5, _sink.Batches[0].Single(s => s.Series.StartsWith(HistorianMetrics.Rain, StringComparison.Ordinal)).Value);
    }

    [Fact]
    public void A_step_carries_the_alarm_counts_after_its_own_conditions()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 25, 30));

        double Count(string metric) => _sink.Batches[^1].Single(s => s.Series.StartsWith(metric + "{", StringComparison.Ordinal)).Value;
        Assert.Equal(1, Count(HistorianMetrics.AlarmsActive));
        Assert.Equal(1, Count(HistorianMetrics.AlarmsUnacknowledged));
    }

    [Fact]
    public void A_message_without_a_new_step_writes_nothing()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 0, 0));
        Do(s => s.PlcData(SparkplugMetric.Double(PlcMetrics.Level("T2"), 1.3)));
        Do(s => s.Step(1, 0, 0));

        Assert.Single(_sink.Batches);
    }

    [Fact]
    public void A_finished_run_writes_its_totals_once_at_the_end_of_the_window()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 10, 12));
        Do(s => s.EndRun(ScenarioStates.Finished, 11_967.0, 12_931.0, RunScript.At(431)));
        Do(s => s.PlcData(SparkplugMetric.Double(PlcMetrics.Level("T2"), 1.3)));

        Assert.Equal(2, _sink.Batches.Count);
        var final = _sink.Batches[1];
        Assert.All(final, s => Assert.Equal(SiteFixture.ClockMs(RunScript.At(431)), s.TimestampMs));
        Assert.Equal(11_967.0, final.Single(s => s.Series.StartsWith(HistorianMetrics.OverflowTotal, StringComparison.Ordinal)).Value);
    }

    [Fact]
    public void Nothing_is_written_while_preparing_or_without_a_run()
    {
        Do(s => s.Step(1, 0, 0));
        Do(s => s.BeginRun(1));
        Do(s => s.Step(1, 0, 0));

        Assert.Empty(_sink.Batches);
    }

    [Fact]
    public void Stale_PLC_values_are_left_out()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.PlcDeath());
        Do(s => s.Step(1, 0, 0));

        var batch = Assert.Single(_sink.Batches);
        Assert.DoesNotContain(batch, s => s.Series.StartsWith(HistorianMetrics.TankLevel, StringComparison.Ordinal));
        Assert.Contains(batch, s => s.Series.StartsWith(HistorianMetrics.OverflowTotal, StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_run_gets_its_own_series()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 0, 0));
        var first = Run;
        _site.Clock.Advance(TimeSpan.FromMinutes(2));
        Do(s => s.BeginRun(2, algorithm: "base"));
        Do(s => s.Running());
        Do(s => s.Step(1, 0, 0));

        Assert.NotEqual(first, Run);
        Assert.Contains($"run=\"{Run}\",storm=\"s1\",algorithm=\"base\"", _sink.Batches[1][0].Series, StringComparison.Ordinal);
    }
}

internal sealed class MemorySampleSink : ISampleSink
{
    public List<IReadOnlyList<Sample>> Batches { get; } = [];

    public void Write(IReadOnlyList<Sample> samples) => Batches.Add(samples);
}
