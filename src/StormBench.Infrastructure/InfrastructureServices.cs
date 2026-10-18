using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Optimization;
using StormBench.Application.Runs;
using StormBench.Application.Scenarios;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Infrastructure.Postgres;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Infrastructure.VictoriaMetrics;
using StormBench.Sparkplug;

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
        public IServiceCollection AddStormBenchInfrastructure(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return services
                .AddHistorian(configuration)
                .AddRunStore(configuration)
                .AddPrimaryHost(configuration);
        }

        private IServiceCollection AddHistorian(IConfiguration configuration)
        {
            services.AddSingleton<IRunSeriesWriter, PrometheusRunSeries>();
            var section = configuration.GetSection(VictoriaMetricsOptions.Section);
            if (section.Get<VictoriaMetricsOptions>() is not { Enabled: true })
            {
                return services.AddSingleton<ISampleSink, NoSampleSink>();
            }
            services.Configure<VictoriaMetricsOptions>(section);
            services.AddHttpClient(VictoriaMetricsWriter.HttpClientName);
            return services
                .AddSingleton<VictoriaMetricsWriter>()
                .AddSingleton<ISampleSink>(sp => sp.GetRequiredService<VictoriaMetricsWriter>())
                .AddHostedService(sp => sp.GetRequiredService<VictoriaMetricsWriter>().Writer);
        }

        private IServiceCollection AddRunStore(IConfiguration configuration)
        {
            if (configuration.GetConnectionString(ScadaDatabase.ConnectionName) is not { } connectionString
                || string.IsNullOrWhiteSpace(connectionString))
            {
                return services
                    .AddSingleton<IRunStore, NoRunStore>()
                    .AddSingleton<IAlarmJournalStore, NoAlarmJournalStore>();
            }
            services.AddNpgsqlDataSource(connectionString);
            return services
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
        private IServiceCollection AddPrimaryHost(IConfiguration configuration)
        {
            services.Configure<HostApplicationOptions>(configuration.GetSection("Mqtt"));
            services.PostConfigure<HostApplicationOptions>(o =>
            {
                o.HostId = string.IsNullOrEmpty(o.HostId) ? Uns.PrimaryHostId : o.HostId;
                o.GroupId = string.IsNullOrEmpty(o.GroupId) ? Uns.GroupId : o.GroupId;
            });
            return services
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
