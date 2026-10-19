using StormBench.Application.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>What reading the run store says when there is none.</summary>
internal static class NoStore
{
    public static StoreUnavailableException NotConfigured() =>
        new($"No run store configured (ConnectionStrings:{ScadaDatabase.ConnectionName}).");
}
