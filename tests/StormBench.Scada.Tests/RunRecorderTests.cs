using StormBench.Application.Runs;
using StormBench.Contracts;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;
using StormBench.Sparkplug.Payloads;

namespace StormBench.Scada.Tests;

/// <summary>Run summaries: one row per run, saved at the start and at the end, compared with the Base Case.</summary>
public class RunRecorderTests
{
    private readonly SiteFixture _site = new();
    private readonly MemoryRunStore _store = new();
    private readonly RunRecorder _runs;

    public RunRecorderTests()
    {
        _runs = new RunRecorder(_store, _site.Clock);
        _site.SimBirth();
    }

    private void Observe() => _runs.Observe(_site.Read());

    private void Do(Action<SiteFixture> message)
    {
        message(_site);
        Observe();
    }

    [Fact]
    public void A_finished_run_is_saved_at_its_start_and_its_end()
    {
        var started = _site.Clock.GetUtcNow();
        Do(s => s.BeginRun(3));
        Do(s => s.Running());
        _site.Clock.Advance(TimeSpan.FromSeconds(40));
        Do(s => s.Step(1, 0, 0));
        Do(s => s.Step(2, 900, 1000, mismatches: 0));
        Do(s => s.EndRun(ScenarioStates.Finished, 11_967.0, 12_931.0, RunScript.At(431)));

        Assert.Equal(2, _store.Saved.Count);
        var first = _store.Saved[0];
        Assert.Equal(new RunId("20260928-120000-3"), first.Id);
        Assert.Equal(RunState.Running, first.State);
        Assert.Equal((new StormId("s1"), "Storm one", new AlgorithmId("efd-center"), "EFD from the center"), (first.Storm, first.StormLabel, first.Algorithm, first.AlgorithmLabel));
        Assert.Equal(new DateTime(2000, 5, 30, 0, 0, 0), first.StormStart);
        Assert.Equal(new DateTime(2000, 5, 31, 12, 0, 0), first.StormEnd);
        Assert.Equal(started, first.StartedAt);
        Assert.Null(first.FinishedAt);

        var last = _store.Saved[1];
        Assert.Equal(first.Id, last.Id);
        Assert.Equal(RunState.Finished, last.State);
        Assert.Equal(started.AddSeconds(40), last.FinishedAt);
        Assert.Equal((2L, 431L), (last.Steps, last.StepsPlanned));
        Assert.Equal(new Volume(11_967.0), last.Overflow);
        Assert.Equal(new Volume(12_931.0), last.BaseCase);
        Assert.Equal(-7.455, last.ChangePercent!.Value, 3);
        Assert.Equal(new Volume(200.0), last.WwtpInflow);
        Assert.Equal(last, _runs.Current);
        Assert.False(_runs.IsOpen);
    }

    [Theory]
    [InlineData(11_967.0, 12_931.0, -7.4550)]
    [InlineData(28_260.0, 28_345.0, -0.2999)]
    [InlineData(25_800.0, 25_659.0, 0.5495)]
    [InlineData(5.0, 5.0, 0.0)]
    public void Change_is_relative_to_the_base_case_in_percent(double overflow, double baseCase, double expected) =>
        Assert.Equal(expected, RunSummary.Change(new Volume(overflow), new Volume(baseCase))!.Value, 4);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(3.0, 0.5)]
    [InlineData(3.0, null)]
    public void Change_is_unknown_while_the_base_case_hardly_spills(double overflow, double? baseCase) =>
        Assert.Null(RunSummary.Change(new Volume(overflow), baseCase is { } b ? new Volume(b) : null));

    [Fact]
    public void Stopped_and_failed_runs_keep_their_state()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 10, 12));
        Do(s => s.EndRun(ScenarioStates.Stopped));
        Do(s => s.BeginRun(2));
        Do(s => s.EndRun(ScenarioStates.Failed));

        Assert.Equal(
            [RunState.Running, RunState.Stopped, RunState.Running, RunState.Failed],
            _store.Saved.Select(r => r.State));
        Assert.Equal(new Volume(10), _store.Saved[1].Overflow);
    }

    [Fact]
    public void A_run_whose_end_was_not_seen_is_closed_as_interrupted()
    {
        Do(s => s.BeginRun(1));
        Do(s => s.Running());
        Do(s => s.Step(1, 10, 12));
        Do(s => s.SimBirth());
        Do(s => s.BeginRun(1));

        Assert.Equal(3, _store.Saved.Count);
        Assert.Equal(RunState.Interrupted, _store.Saved[1].State);
        Assert.NotNull(_store.Saved[1].FinishedAt);
        Assert.Equal(RunState.Running, _store.Saved[2].State);
        Assert.True(_runs.IsOpen);
    }

    [Fact]
    public void A_run_that_ended_before_Scada_started_is_not_recorded()
    {
        _site.SimData(SparkplugMetric.Int64(SimMetrics.Run, 4), SparkplugMetric.String(SimMetrics.State, ScenarioStates.Finished));
        Observe();
        Do(s => s.Step(5, 10, 12));

        Assert.Empty(_store.Saved);
        Assert.Null(_runs.Current);
    }

    [Fact]
    public void Nothing_is_recorded_while_the_simulator_is_offline()
    {
        Do(s => s.SimDeath());
        Do(s => s.BeginRun(1));

        Assert.Empty(_store.Saved);
    }
}

/// <summary>Keeps what the recorder saves, in order.</summary>
internal sealed class MemoryRunStore : IRunStore
{
    public List<RunSummary> Saved { get; } = [];

    public void Save(RunSummary run) => Saved.Add(run);

    public Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunSummary>>([.. Saved]);
}
