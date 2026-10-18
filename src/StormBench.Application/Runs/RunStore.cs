using StormBench.Domain.Runs;

namespace StormBench.Application.Runs;

/// <summary>Where run summaries are kept. <see cref="Save"/> only queues: it is called on the UNS host's path.</summary>
public interface IRunStore
{
    void Save(RunSummary run);

    /// <summary>Newest first. Throws <see cref="StoreUnavailableException"/> without a store.</summary>
    Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>The run store is not configured or cannot be reached; the message says which.</summary>
public sealed class StoreUnavailableException : Exception
{
    public StoreUnavailableException()
    {
    }

    public StoreUnavailableException(string message)
        : base(message)
    {
    }

    public StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The runs of the test console, newest first.</summary>
public sealed class ListRunsHandler(IRunStore runs)
{
    public const int Limit = 500;

    public Task<IReadOnlyList<RunSummary>> HandleAsync(CancellationToken cancellationToken) => runs.ListAsync(Limit, cancellationToken);
}
