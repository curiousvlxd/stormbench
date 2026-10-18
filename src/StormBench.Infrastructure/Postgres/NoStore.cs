using StormBench.Application.Alarms;
using StormBench.Application.Runs;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>No database configured: runs are not kept, and reading them says so.</summary>
public sealed class NoRunStore : IRunStore
{
    public void Save(RunSummary run)
    {
    }

    public Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<RunSummary>>(NoStore.NotConfigured());
}

/// <summary>No database configured: the journal is not kept, and reading it says so.</summary>
public sealed class NoAlarmJournalStore : IAlarmJournalStore
{
    public void Append(JournalEntry entry)
    {
    }

    public Task<IReadOnlyList<JournalEntry>> ReadAsync(RunId? run, int limit, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<JournalEntry>>(NoStore.NotConfigured());
}

internal static class NoStore
{
    public static StoreUnavailableException NotConfigured() =>
        new($"No run store configured (ConnectionStrings:{ScadaDatabase.ConnectionName}).");
}
