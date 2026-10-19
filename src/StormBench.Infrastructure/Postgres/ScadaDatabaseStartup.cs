using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace StormBench.Infrastructure.Postgres;

/// <summary>Creates the schema at startup, retrying while PostgreSQL is still starting; never holds up Scada. Idle without a store.</summary>
public sealed partial class ScadaDatabaseStartup(ScadaDatabase database, ILogger<ScadaDatabaseStartup> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!database.Enabled)
        {
            return;
        }
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await database.EnsureSchemaAsync(stoppingToken);
                return;
            }
            catch (NpgsqlException ex)
            {
                if (attempt is 1 || attempt % 12 is 0)
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
