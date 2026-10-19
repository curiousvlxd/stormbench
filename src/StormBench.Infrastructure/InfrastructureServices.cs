using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Optimization;
using StormBench.Application.Runs;
using StormBench.Application.Scenarios;
using StormBench.Application.Site;
using StormBench.Infrastructure.Postgres;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Infrastructure.VictoriaMetrics;
using StormBench.Sparkplug.HostApplication;

namespace StormBench.Infrastructure;

public static class InfrastructureServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The adapters behind the application's ports. The historian and the run store are
        /// optional (ADR 0015): VictoriaMetrics when <c>VictoriaMetrics:Url</c> is set, PostgreSQL
        /// when <c>ConnectionStrings:runs</c> is. The UNS host worker is registered last, so the
        /// background writers start before it and flush after it has stopped.
        /// </summary>
        public IServiceCollection AddStormBenchInfrastructure() =>
            services
                .AddHistorian()
                .AddRunStore()
                .AddPrimaryHost();

        private IServiceCollection AddHistorian()
        {
            services.AddHttpClient(VictoriaMetricsWriter.HttpClientName);
            return services
                .ConfigureOptions<VictoriaMetricsOptionsSetup>()
                .AddSingleton<IRunSeriesWriter, PrometheusRunSeries>()
                .AddSingleton<VictoriaMetricsWriter>()
                .AddSingleton<ISampleSink>(sp => sp.GetRequiredService<VictoriaMetricsWriter>())
                .AddHostedService(sp => sp.GetRequiredService<VictoriaMetricsWriter>().Writer);
        }

        /// <summary>
        /// The run store from <see cref="RunStoreOptions"/>; without a connection string it keeps
        /// nothing. Its health check reports a store that does not answer as degraded.
        /// </summary>
        private IServiceCollection AddRunStore()
        {
            services.AddHealthChecks().AddCheck<RunStoreHealthCheck>(ScadaDatabase.ConnectionName, HealthStatus.Degraded);
            return services
                .ConfigureOptions<RunStoreOptionsSetup>()
                .AddSingleton<ScadaDatabase>()
                .AddHostedService<ScadaDatabaseStartup>()
                .AddSingleton<PostgresRunStore>()
                .AddSingleton<IRunStore>(sp => sp.GetRequiredService<PostgresRunStore>())
                .AddHostedService(sp => sp.GetRequiredService<PostgresRunStore>().Writer)
                .AddSingleton<PostgresAlarmJournal>()
                .AddSingleton<IAlarmJournalStore>(sp => sp.GetRequiredService<PostgresAlarmJournal>())
                .AddHostedService(sp => sp.GetRequiredService<PostgresAlarmJournal>().Writer);
        }

        /// <summary>The Sparkplug Primary Host session on the broker from <c>Mqtt</c>, the UNS state it keeps and its worker.</summary>
        private IServiceCollection AddPrimaryHost()
        {
            return services
                .ConfigureOptions<HostApplicationOptionsSetup>()
                .AddSingleton(sp => sp.GetRequiredService<IOptions<HostApplicationOptions>>().Value)
                .AddSingleton<SparkplugHostApplication>()
                .AddSingleton<IBrokerConnection, PrimaryHostConnection>()
                .AddSingleton<UnsState>()
                .AddSingleton<UnsSiteReader>()
                .AddSingleton<ISiteReader>(sp => sp.GetRequiredService<UnsSiteReader>())
                .AddSingleton<ISimulatorCommands, UnsSimulatorCommands>()
                .AddSingleton<IPlcCommands, UnsPlcCommands>()
                .AddHostedService<UnsHostWorker>();
        }
    }
}
