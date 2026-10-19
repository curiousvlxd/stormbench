using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace StormBench.Infrastructure.Postgres;

/// <summary>
/// Whether the run store answers. Without a store Scada is healthy and says it keeps no history;
/// an unreachable store is degraded, not unhealthy: Scada runs on without the history.
/// </summary>
internal sealed class RunStoreHealthCheck(ScadaDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!database.Enabled)
        {
            return HealthCheckResult.Healthy("No run store configured; no history is kept.");
        }
        try
        {
            await database.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException ex)
        {
            return HealthCheckResult.Degraded("Run store not reachable.", ex);
        }
    }
}
