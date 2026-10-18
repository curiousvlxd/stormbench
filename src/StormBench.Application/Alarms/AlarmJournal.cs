using StormBench.Application.Runs;
using StormBench.Domain.Alarms;
using StormBench.Domain.Runs;

namespace StormBench.Application.Alarms;

/// <summary>
/// One ISA-18.2 state change as the journal keeps it, with the run it happened in. Id is the
/// store's sequence (0 before it is written).
/// </summary>
public sealed record JournalEntry(
    long Id,
    RunId? Run,
    string AlarmId,
    string Source,
    string Message,
    AlarmPriority Priority,
    AlarmState From,
    AlarmState To,
    DateTimeOffset WallTime,
    DateTime? SimTime);

/// <summary>Where journal entries are kept. <see cref="Append"/> only queues.</summary>
public interface IAlarmJournalStore
{
    void Append(JournalEntry entry);

    /// <summary>
    /// Entries of one run (all runs when null), oldest first, at most <paramref name="limit"/> of the
    /// newest. Throws <see cref="StoreUnavailableException"/> without a store.
    /// </summary>
    Task<IReadOnlyList<JournalEntry>> ReadAsync(RunId? run, int limit, CancellationToken cancellationToken);
}

/// <summary>The run that alarm transitions belong to: the open run, or the last one.</summary>
public interface ICurrentRun
{
    RunId? CurrentRunId { get; }
}

/// <summary>The alarm journal: every state change of the alarm engine, stamped with the current run.</summary>
public sealed class AlarmJournal(IAlarmJournalStore store, ICurrentRun currentRun) : IAlarmObserver
{
    public void OnTransition(AlarmTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var d = transition.Definition;
        store.Append(new JournalEntry(
            0, currentRun.CurrentRunId, d.Id, d.Source, d.Message, d.Priority, transition.From, transition.To, transition.WallTime, transition.SimTime));
    }
}

/// <summary>The journal of one run, or of all runs.</summary>
public sealed record ReadAlarmJournal(RunId? Run);

public sealed class ReadAlarmJournalHandler(IAlarmJournalStore journal)
{
    public const int Limit = 5_000;

    public Task<IReadOnlyList<JournalEntry>> HandleAsync(ReadAlarmJournal query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return journal.ReadAsync(query.Run, Limit, cancellationToken);
    }
}
