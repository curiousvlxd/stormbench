using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StormBench.Application.Runs;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>
/// The run store in PostgreSQL (ADR 0015): one row per run of the test console and the alarm
/// journal. The schema is versioned (<see cref="SchemaMigrations"/>) and brought up to date once per
/// process, before anything is read or written. Optional: without the connection string
/// <see cref="ConnectionName"/> it is not <see cref="Enabled"/> and Scada keeps no history.
/// </summary>
public sealed partial class ScadaDatabase : IDisposable
{
    /// <summary>ConnectionStrings:runs, as Aspire names it for a database resource called "runs".</summary>
    public const string ConnectionName = "runs";

    /// <summary>Taken for the length of a migration, so that two Scadas starting together do not both migrate.</summary>
    private const long MigrationLock = 0x5374_6f72_6d42;

    private const string MigrationsTable = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version    integer PRIMARY KEY,
            name       text NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now()
        )
        """;

    private const string CloseInterrupted = $"UPDATE runs SET state = '{RunStateText.Interrupted}' WHERE finished_at IS NULL";

    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<ScadaDatabase> _logger;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _ready;

    public ScadaDatabase(IOptions<RunStoreOptions> options, ILogger<ScadaDatabase> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        if (options.Value.Enabled)
        {
            var builder = new NpgsqlDataSourceBuilder(options.Value.ConnectionString) { Name = ConnectionName };
            _dataSource = builder.Build();
        }
    }

    public bool Enabled => _dataSource is not null;

    /// <summary>An open connection, with the schema up to date. Throws <see cref="StoreUnavailableException"/> without a store.</summary>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var dataSource = _dataSource ?? throw NoStore.NotConfigured();
        await EnsureSchemaAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>One round trip, without touching the schema: does PostgreSQL answer?</summary>
    public async Task PingAsync(CancellationToken cancellationToken)
    {
        var dataSource = _dataSource ?? throw NoStore.NotConfigured();
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// Applies the migrations the database has not seen, in one transaction under an advisory
    /// lock, once per process. Runs that a previous Scada left open are closed as
    /// <see cref="RunState.Interrupted"/>: nobody will finish them.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_ready || _dataSource is null)
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
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Lock)", new { Lock = MigrationLock }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(MigrationsTable, transaction: transaction, cancellationToken: cancellationToken));
            var applied = (await connection.QueryAsync<int>(new CommandDefinition("SELECT version FROM schema_migrations", transaction: transaction, cancellationToken: cancellationToken))).ToHashSet();
            foreach (var migration in SchemaMigrations.All.Where(m => !applied.Contains(m.Version)))
            {
                await connection.ExecuteAsync(new CommandDefinition(migration.Sql, transaction: transaction, cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_migrations (version, name) VALUES (@Version, @Name)", new { migration.Version, migration.Name }, transaction, cancellationToken: cancellationToken));
                LogMigrated(migration.Version, migration.Name);
            }
            await connection.ExecuteAsync(new CommandDefinition(CloseInterrupted, transaction: transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            _ready = true;
            LogReady();
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    public void Dispose()
    {
        _dataSource?.Dispose();
        _schemaGate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run store schema: applied migration {Version} ({Name})")]
    private partial void LogMigrated(int version, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run store ready (PostgreSQL)")]
    private partial void LogReady();
}
