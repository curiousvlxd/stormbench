using Microsoft.Extensions.DependencyInjection;
using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Kpi;
using StormBench.Application.Optimization;
using StormBench.Application.Runs;
using StormBench.Application.Scenarios;
using StormBench.Application.Site;

namespace StormBench.Application;

public static class ApplicationServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The use cases and the state they share. The ports (site, broker, commands, stores,
        /// series writer) come from the infrastructure; the EFD parameters from configuration.
        /// </summary>
        public IServiceCollection AddStormBenchApplication() => services
            .AddSingleton<AlarmEngine>()
            .AddSingleton<FieldChecks>()
            .AddSingleton<IAlarmObserver, AlarmJournal>()
            .AddSingleton<RunRecorder>()
            .AddSingleton<ICurrentRun>(sp => sp.GetRequiredService<RunRecorder>())
            .AddSingleton<RunTrend>()
            .AddSingleton<RunSampler>()
            .AddSingleton<SiteMonitor>()
            .AddSingleton<OptimizationMetrics>()
            .AddSingleton<CenterOptimizer>()
            .AddSingleton<GetScenarioStatusHandler>()
            .AddSingleton<StartStormHandler>()
            .AddSingleton<StopStormHandler>()
            .AddSingleton<SetCenterLinkHandler>()
            .AddSingleton<JamGateHandler>()
            .AddSingleton<FreezeSensorHandler>()
            .AddSingleton<AcknowledgeAlarmHandler>()
            .AddSingleton<ReadAlarmJournalHandler>()
            .AddSingleton<ListRunsHandler>()
            .AddSingleton<GetKpiHandler>()
            .AddSingleton<GetOptimizationStatusHandler>();
    }
}
