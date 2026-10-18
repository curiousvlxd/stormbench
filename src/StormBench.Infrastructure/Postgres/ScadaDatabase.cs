using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>
/// The run store in PostgreSQL (ADR 0015): one row per run of the test console and the alarm
/// journal, two plain tables created on startup. Optional: without the connection string
/// <see cref="ConnectionName"/> Scada runs as before and keeps no history.
/// </summary>
public sealed partial class ScadaDatabase(NpgsqlDataSource dataSource, ILogger<ScadaDatabase> logger) : IDisposable
{
    /// <summary>ConnectionStrings:runs, as Aspire names it for a database resource called "runs".</summary>
    public const string ConnectionName = "runs";

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS runs (
            id              text PRIMARY KEY,
            sim_run         bigint NOT NULL,
            storm           text NOT NULL,
            storm_label     text,
            algorithm       text NOT NULL,
            algorithm_label text,
            pace_s          double precision,
            state           text NOT NULL,
            storm_start     timestamp,
            storm_end       timestamp,
            started_at      timestamptz NOT NULL,
            finished_at     timestamptz,
            steps           bigint NOT NULL,
            steps_planned   bigint NOT NULL,
            overflow_m3     double precision NOT NULL,
            base_case_m3    double precision,
            change_pct      double precision,
            wwtp_inflow_m3  double precision,
            mismatches      bigint NOT NULL
        );
        CREATE TABLE IF NOT EXISTS alarm_journal (
            id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            run_id      text,
            alarm_id    text NOT NULL,
            source      text NOT NULL,
            message     text NOT NULL,
            priority    smallint NOT NULL,
            from_state  text NOT NULL,
            to_state    text NOT NULL,
            wall_time   timestamptz NOT NULL,
            sim_time    timestamp
        );
        CREATE INDEX IF NOT EXISTS alarm_journal_run ON alarm_journal (run_id, id);
        """;

    private const string Startup =
        $"{Schema}\nUPDATE runs SET state = '{RunStateText.Interrupted}' WHERE finished_at IS NULL;";

    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _ready;

    public NpgsqlDataSource DataSource => dataSource;

    /// <summary>
    /// Creates the tables once per process, before anything is written. Runs that a previous
    /// Scada left open are closed as <see cref="RunState.Interrupted"/>: nobody will finish them.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
            {
                return;
            }
            await using var command = dataSource.CreateCommand(Startup);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _ready = true;
            LogReady();
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    public void Dispose() => _schemaGate.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Run store ready (PostgreSQL)")]
    private partial void LogReady();
}

/// <summary>Creates the schema at startup, retrying while PostgreSQL is still starting; never holds up Scada.</summary>
public sealed partial class ScadaDatabaseStartup(ScadaDatabase database, ILogger<ScadaDatabaseStartup> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await database.EnsureSchemaAsync(stoppingToken);
                return;
            }
            catch (NpgsqlException ex)
            {
                if (attempt == 1 || attempt % 12 == 0)
                {
                    LogUnavailable(ex.Message);
                }
            }
            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run store not reachable yet, retrying: {Reason}")]
    private partial void LogUnavailable(string reason);
}
