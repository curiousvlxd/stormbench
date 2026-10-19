using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StormBench.Infrastructure.VictoriaMetrics;

/// <summary>
/// Writes samples to VictoriaMetrics in the background: batches in the Prometheus text format,
/// posted to <c>/api/v1/import/prometheus</c>. A batch that fails is logged and dropped; the UNS
/// host never waits for it. Without a URL in <see cref="VictoriaMetricsOptions"/> it keeps nothing.
/// https://docs.victoriametrics.com/victoriametrics/single-server-victoriametrics/#how-to-import-data-in-prometheus-exposition-format
/// </summary>
public sealed class VictoriaMetricsWriter : ISampleSink
{
    public const string HttpClientName = "victoriametrics";

    private readonly IHttpClientFactory _http;
    private readonly Uri? _import;

    public VictoriaMetricsWriter(IOptions<VictoriaMetricsOptions> options, IHttpClientFactory http, ILogger<VictoriaMetricsWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _import = options.Value.Enabled ? new Uri(options.Value.BaseUrl, "api/v1/import/prometheus") : null;
        Writer = new BatchWriter<Sample>("Historian (VictoriaMetrics)", PostAsync, logger);
    }

    /// <summary>Hosted service that posts the queued samples.</summary>
    public BatchWriter<Sample> Writer { get; }

    public void Write(IReadOnlyList<Sample> samples)
    {
        if (_import is not null)
        {
            Writer.EnqueueRange(samples);
        }
    }

    private async Task PostAsync(IReadOnlyList<Sample> samples, CancellationToken cancellationToken)
    {
        using var content = new StringContent(PrometheusText.Format(samples), Encoding.UTF8, "text/plain");
        var client = _http.CreateClient(HttpClientName);
        using var response = await client.PostAsync(_import, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// The section <c>VictoriaMetrics</c>; its <see cref="Url"/> is shared with the OpenTelemetry export (ADR 0016).
/// </summary>
public sealed class VictoriaMetricsOptions
{
    public const string Section = "VictoriaMetrics";

    /// <summary>Base URL, kept with a trailing slash so that relative API paths resolve below it.</summary>
    public string? Url
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) || value.EndsWith('/') ? value : value + "/";
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(Url);

    public Uri BaseUrl => new(Url ?? throw new InvalidOperationException($"{Section}:{nameof(Url)} is not set"));
}
