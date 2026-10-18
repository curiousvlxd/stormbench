using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    /// <summary>Base URL of VictoriaMetrics; metrics go to its OTLP endpoint (ADR 0016). Scada's historian uses it too.</summary>
    public const string VictoriaMetricsUrlKey = "VictoriaMetrics:Url";

    /// <summary>Base URL of VictoriaLogs; logs go to its OTLP endpoint (ADR 0016).</summary>
    public const string VictoriaLogsUrlKey = "VictoriaLogs:Url";

    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const int VictoriaMetricsExportIntervalMs = 10_000;

    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddServiceDefaults()
        {
            builder.ConfigureOpenTelemetry();
            builder.AddDefaultHealthChecks();
            builder.Services.AddServiceDiscovery();
            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
            return builder;
        }

        public TBuilder ConfigureOpenTelemetry()
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation())
                .WithTracing(tracing => tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(aspNetCore => aspNetCore.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation());

            builder.AddOpenTelemetryExporters();
            return builder;
        }

        public TBuilder AddDefaultHealthChecks()
        {
            builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
            return builder;
        }

        private void AddOpenTelemetryExporters()
        {
            var otel = builder.Services.AddOpenTelemetry();

            if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            {
                otel.WithLogging(logging => logging.AddOtlpExporter())
                    .WithMetrics(metrics => metrics.AddOtlpExporter())
                    .WithTracing(tracing => tracing.AddOtlpExporter());
            }

            if (BaseUrl(builder.Configuration[VictoriaMetricsUrlKey]) is { } victoriaMetrics)
            {
                otel.WithMetrics(metrics => metrics.AddOtlpExporter("victoriametrics", (exporter, reader) =>
                {
                    Direct(exporter, new Uri(victoriaMetrics, "opentelemetry/v1/metrics"));
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = VictoriaMetricsExportIntervalMs;
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                }));
            }
            if (BaseUrl(builder.Configuration[VictoriaLogsUrlKey]) is { } victoriaLogs)
            {
                otel.WithLogging(logging => logging.AddOtlpExporter(
                    "victorialogs", exporter => Direct(exporter, new Uri(victoriaLogs, "insert/opentelemetry/v1/logs"))));
            }
        }
    }

    extension(WebApplication app)
    {
        public WebApplication MapDefaultEndpoints()
        {
            if (app.Environment.IsDevelopment())
            {
                app.MapHealthChecks(HealthEndpointPath);
                app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
            }
            return app;
        }
    }

    /// <summary>
    /// OTLP over HTTP straight to a store: the full signal URL, none of the dashboard's headers,
    /// and a plain HttpClient. Through the IHttpClientFactory defaults above every export would
    /// be logged and retried, and those logs exported again.
    /// </summary>
    private static void Direct(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        exporter.Headers = null;
        var timeout = TimeSpan.FromMilliseconds(exporter.TimeoutMilliseconds);
        exporter.HttpClientFactory = () => new HttpClient { Timeout = timeout };
    }

    private static Uri? BaseUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : new Uri(url.EndsWith('/') ? url : url + "/");
}

public static class OptionsValueExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The bound value of <typeparamref name="TOptions"/> as a service of its own, for classes
        /// of the shared libraries that take their options as a plain object.
        /// </summary>
        public IServiceCollection AddOptionsValue<TOptions>()
            where TOptions : class =>
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<TOptions>>().Value);
    }
}
