namespace StormBench.Infrastructure.Postgres;

/// <summary>Where the run store lives; without a connection string Scada keeps no history (ADR 0015).</summary>
public sealed class RunStoreOptions
{
    public string? ConnectionString { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);
}
