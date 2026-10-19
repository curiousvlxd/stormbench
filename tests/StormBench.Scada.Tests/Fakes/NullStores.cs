using StormBench.Application.Runs;
using StormBench.Domain.Runs;
using StormBench.Infrastructure.VictoriaMetrics;

namespace StormBench.Scada.Tests.Fakes;

/// <summary>A run store that keeps nothing, for tests that do not look at the history.</summary>
internal sealed class NullRunStore : IRunStore
{
    public void Save(RunSummary run)
    {
    }

    public Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunSummary>>([]);
}

/// <summary>A historian that keeps nothing.</summary>
internal sealed class NullSampleSink : ISampleSink
{
    public void Write(IReadOnlyList<Sample> samples)
    {
    }
}
