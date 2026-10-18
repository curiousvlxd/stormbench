using Microsoft.Extensions.Time.Testing;
using StormBench.Application.Alarms;
using StormBench.Domain.Alarms;
using StormBench.Domain.Runs;

namespace StormBench.Scada.Tests;

/// <summary>The alarm journal: every ISA-18.2 state change, with wall time, simulated time and run.</summary>
public class AlarmJournalTests : ICurrentRun
{
    private static readonly AlarmDefinition High = new("T1.Overflow", "T1", "Tank T1 overflowing", AlarmPriority.High);
    private static readonly AlarmDefinition Low = new("PLC01.Clamped", "PLC01", "Setpoint limited", AlarmPriority.Low);
    private static readonly DateTime Sim = new(2000, 5, 30, 22, 25, 0);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryJournalStore _store = new();
    private readonly AlarmEngine _engine;
    private RunId? _run = new RunId("run-a");

    public AlarmJournalTests() => _engine = new AlarmEngine(_clock, [new AlarmJournal(_store, this)]);

    RunId? ICurrentRun.CurrentRunId => _run;

    private IEnumerable<(AlarmState, AlarmState)> Changes(string id) =>
        _store.Entries.Where(e => e.AlarmId == id).Select(e => (e.From, e.To));

    [Fact]
    public void Raise_acknowledge_and_clear_are_three_entries()
    {
        _engine.Apply([(High, true)], Sim);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _engine.Acknowledge(High.Id);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _engine.Apply([(High, false)], Sim.AddMinutes(10));

        Assert.Equal(
            [
                (AlarmState.Normal, AlarmState.Unacknowledged),
                (AlarmState.Unacknowledged, AlarmState.Acknowledged),
                (AlarmState.Acknowledged, AlarmState.Normal),
            ],
            Changes(High.Id));
        var raised = _store.Entries[0];
        Assert.Equal((new RunId("run-a"), "T1", "Tank T1 overflowing", AlarmPriority.High), (raised.Run, raised.Source, raised.Message, raised.Priority));
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), raised.WallTime);
        Assert.Equal(Sim, raised.SimTime);
        Assert.Equal(_clock.GetUtcNow(), _store.Entries[2].WallTime);
        Assert.Equal(Sim.AddMinutes(10), _store.Entries[2].SimTime);
    }

    [Fact]
    public void An_acknowledgement_carries_the_simulated_time_last_seen()
    {
        _engine.Apply([(High, true)], Sim);
        _engine.Apply([(Low, false)], Sim.AddMinutes(5));
        _engine.Acknowledge(High.Id);

        Assert.Equal(Sim.AddMinutes(5), _store.Entries[^1].SimTime);
    }

    [Fact]
    public void Return_before_acknowledge_and_a_new_activation_are_journaled()
    {
        _engine.Apply([(High, true)], Sim);
        _engine.Apply([(High, false)], Sim.AddMinutes(5));
        _engine.Apply([(High, true)], Sim.AddMinutes(10));
        _engine.Apply([(High, false)], Sim.AddMinutes(15));
        _engine.AcknowledgeAll();

        Assert.Equal(
            [
                (AlarmState.Normal, AlarmState.Unacknowledged),
                (AlarmState.Unacknowledged, AlarmState.ReturnedUnacknowledged),
                (AlarmState.ReturnedUnacknowledged, AlarmState.Unacknowledged),
                (AlarmState.Unacknowledged, AlarmState.ReturnedUnacknowledged),
                (AlarmState.ReturnedUnacknowledged, AlarmState.Normal),
            ],
            Changes(High.Id));
    }

    [Fact]
    public void Conditions_that_do_not_change_the_state_write_nothing()
    {
        _engine.Apply([(High, false), (Low, false)], Sim);
        _engine.Apply([(High, true)], Sim);
        _engine.Apply([(High, true)], Sim.AddMinutes(5));
        _engine.Acknowledge(Low.Id);
        _engine.Acknowledge(High.Id);
        _engine.Acknowledge(High.Id);

        Assert.Equal(2, _store.Entries.Count);
    }

    [Fact]
    public void Each_entry_belongs_to_the_run_current_at_the_time()
    {
        _run = null;
        _engine.Apply([(Low, true)], Sim);
        _run = new RunId("run-b");
        _engine.Apply([(High, true)], Sim);

        Assert.Equal([null, new RunId("run-b")], _store.Entries.Select(e => e.Run));
    }
}

internal sealed class MemoryJournalStore : IAlarmJournalStore
{
    public List<JournalEntry> Entries { get; } = [];

    public void Append(JournalEntry entry) => Entries.Add(entry);

    public Task<IReadOnlyList<JournalEntry>> ReadAsync(RunId? run, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JournalEntry>>([.. Entries.Where(e => run is null || e.Run == run)]);
}
