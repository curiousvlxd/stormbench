using StormBench.Domain.Control;
using StormBench.Domain.Kpi;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Domain.Tests;

/// <summary>A run of the test console from its start to its end, and the overflow against the Base Case.</summary>
public class RunTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 17, 14, 25, 30, TimeSpan.Zero);

    private static readonly ScenarioCatalog Catalog = new(
        [new StormWindow(new StormId("s1"), "Storm one", "d", new DateTime(2000, 5, 30), new DateTime(2000, 5, 31, 12, 0, 0), 431)],
        [new ControlAlgorithm(new AlgorithmId("efd-center"), "EFD from the center", "d", ControlMode.EfdCenter, ControlCenter.Scada)],
        [0.1],
        new StormId("s1"),
        new AlgorithmId("efd-center"),
        0.1);

    [Fact]
    public void A_run_id_is_the_start_in_utc_and_the_simulators_run_number() =>
        Assert.Equal(new RunId("20261017-142530-3"), RunId.For(Started.ToOffset(TimeSpan.FromHours(3)), 3));

    [Fact]
    public void A_run_begins_with_the_labels_and_window_of_its_storm()
    {
        var run = RunSummary.Begin(3, new StormId("s1"), new AlgorithmId("efd-center"), 0.1, Catalog, Started);

        Assert.Equal((RunState.Running, "Storm one", "EFD from the center"), (run.State, run.StormLabel, run.AlgorithmLabel));
        Assert.Equal((new DateTime(2000, 5, 30), new DateTime(2000, 5, 31, 12, 0, 0)), (run.StormStart, run.StormEnd));
        Assert.Equal((0L, Volume.Zero, (Volume?)null, (DateTimeOffset?)null), (run.Steps, run.Overflow, run.BaseCase, run.FinishedAt));
    }

    [Fact]
    public void A_storm_outside_the_catalog_has_no_labels()
    {
        var run = RunSummary.Begin(1, new StormId("x"), new AlgorithmId("y"), null, null, Started);

        Assert.Equal(((string?)null, (string?)null, (DateTime?)null), (run.StormLabel, run.AlgorithmLabel, run.StormStart));
    }

    [Fact]
    public void Progress_keeps_what_a_report_leaves_out_and_compares_with_the_base_case()
    {
        var run = RunSummary.Begin(3, new StormId("s1"), new AlgorithmId("efd-center"), 0.1, Catalog, Started)
            .Progress(2, 431, new Volume(900), new Volume(1_000), new Volume(200), 0)
            .Progress(3, null, null, null, null, null);

        Assert.Equal((3L, 431L, new Volume(900), (Volume?)new Volume(1_000), (Volume?)new Volume(200), 0L), (run.Steps, run.StepsPlanned, run.Overflow, run.BaseCase, run.WwtpInflow, run.Mismatches));
        Assert.Equal(-10.0, run.ChangePercent!.Value, 10);
    }

    [Fact]
    public void A_closed_run_keeps_how_it_ended()
    {
        var run = RunSummary.Begin(3, new StormId("s1"), new AlgorithmId("efd-center"), 0.1, Catalog, Started).Close(RunState.Stopped, Started.AddMinutes(2));

        Assert.Equal((RunState.Stopped, (DateTimeOffset?)Started.AddMinutes(2)), (run.State, run.FinishedAt));
    }

    [Theory]
    [InlineData(ScenarioState.Finished, RunState.Finished)]
    [InlineData(ScenarioState.Stopped, RunState.Stopped)]
    [InlineData(ScenarioState.Failed, RunState.Failed)]
    [InlineData(ScenarioState.Running, null)]
    [InlineData(ScenarioState.Preparing, null)]
    [InlineData(ScenarioState.Idle, null)]
    public void Only_an_ended_storm_ends_the_run(ScenarioState state, RunState? ended) => Assert.Equal(ended, RunSummary.Ended(state));

    [Fact]
    public void Change_is_unknown_while_the_base_case_hardly_spills()
    {
        Assert.Null(RunSummary.Change(new Volume(3.0), new Volume(0.5)));
        Assert.Null(RunSummary.Change(new Volume(3.0), null));
        Assert.Equal(0.0, RunSummary.Change(new Volume(5.0), new Volume(5.0)));
    }

    [Theory]
    [InlineData(12_069.95, 13_475.86, Comparison.Better)]
    [InlineData(1_005.0, 1_000.0, Comparison.Same)]
    [InlineData(995.5, 1_000.0, Comparison.Same)]
    [InlineData(1_020.0, 1_000.0, Comparison.Worse)]
    [InlineData(0.2, 0.0, Comparison.Same)]
    [InlineData(40.0, 0.0, Comparison.Worse)]
    public void Overflow_is_compared_with_the_base_case_within_a_band(double total, double baseCase, Comparison comparison) =>
        Assert.Equal(comparison, OverflowKpi.Compare(true, new Volume(total), new Volume(baseCase), [], 10, null).Comparison);

    [Fact]
    public void The_kpi_carries_delta_and_relative_change()
    {
        var kpi = OverflowKpi.Compare(true, new Volume(900), new Volume(1_000), [new OverflowPointId("T1")], 10, new Volume(5));

        Assert.Equal(new Volume(-100), kpi.Delta);
        Assert.Equal(-0.1, kpi.Relative!.Value, 12);
        Assert.Equal([new OverflowPointId("T1")], kpi.Overflowing);
        Assert.Equal((10, (Volume?)new Volume(5)), (kpi.Points, kpi.WwtpInflow));
    }

    [Fact]
    public void Without_a_run_a_total_or_a_reference_there_is_nothing_to_compare()
    {
        Assert.Equal((Comparison.NoData, Volume.Zero), Pick(OverflowKpi.Compare(false, new Volume(5), new Volume(5), [], 0, null)));
        Assert.Equal((Comparison.NoData, Volume.Zero), Pick(OverflowKpi.Compare(true, null, new Volume(5), [], 0, null)));
        Assert.Equal((Comparison.NoData, new Volume(5)), Pick(OverflowKpi.Compare(true, new Volume(5), null, [], 0, null)));
        Assert.Null(OverflowKpi.Compare(true, new Volume(40), new Volume(0), [], 0, null).Relative);
    }

    private static (Comparison, Volume) Pick(OverflowKpi kpi) => (kpi.Comparison, kpi.Total);
}
