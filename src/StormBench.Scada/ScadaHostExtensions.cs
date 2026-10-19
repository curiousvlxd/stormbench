using Microsoft.Extensions.FileProviders;
using Npgsql;
using StormBench.Application;
using StormBench.Application.Optimization;
using StormBench.Infrastructure;
using StormBench.Scada.Endpoints;
using StormBench.Scada.Hmi;
using StormBench.Scada.Optimization;

namespace StormBench.Scada;

/// <summary>Composition root of Scada: the use cases, their adapters, the HMI and the HTTP API.</summary>
public static class ScadaHostExtensions
{
    /// <summary>The meter Npgsql reports the run store's commands and connections on.</summary>
    private const string NpgsqlMeter = "Npgsql";

    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddScada()
        {
            builder.AddServiceDefaults();
            builder.Services.AddOpenTelemetry()
                .WithMetrics(m => m.AddMeter(OptimizationMetrics.MeterName, NpgsqlMeter))
                .WithTracing(t => t.AddNpgsql());
            builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.AddDomainConverters());
            builder.Services
                .AddSingleton(TimeProvider.System)
                .ConfigureOptions<EfdParametersSetup>()
                .AddStormBenchApplication()
                .AddHmi()
                .AddStormBenchInfrastructure();
            return builder;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseScada()
        {
            app.MapDefaultEndpoints();
            var fonts = Path.Combine(AppContext.BaseDirectory, "fonts");
            if (Directory.Exists(fonts))
            {
                app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(fonts), RequestPath = "/fonts" });
            }
            app.UseAntiforgery();
            app.MapStaticAssets();
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            app.MapScadaApi();
            return app;
        }
    }
}
