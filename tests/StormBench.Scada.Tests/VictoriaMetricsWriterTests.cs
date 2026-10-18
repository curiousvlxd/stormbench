using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormBench.Infrastructure.VictoriaMetrics;

namespace StormBench.Scada.Tests;

/// <summary>
/// ADR 0015 against a real VictoriaMetrics: samples stamped with model time (the year 2000) are
/// kept and read back when the retention covers them.
/// </summary>
public sealed class VictoriaMetricsWriterTests : IAsyncLifetime, IDisposable
{
    private const string Image =
        "victoriametrics/victoria-metrics:v1.153.0@sha256:5eff7af5341e401471002f58d106d399e614a62f3d240f2dbc21901e49eed5dd";

    private static readonly long StormStartMs = SiteFixture.ClockMs(new DateTime(2000, 5, 30, 0, 0, 0));

    private readonly IContainer _vm = new ContainerBuilder(Image)
        .WithCommand("-retentionPeriod=100y", "-search.disableCache")
        .WithPortBinding(8428, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8428).ForPath("/health")))
        .Build();

    private readonly HttpClient _http = new();

    public async ValueTask InitializeAsync() => await _vm.StartAsync();

    public async ValueTask DisposeAsync() => await _vm.DisposeAsync();

    public void Dispose() => _http.Dispose();

    private Uri BaseUrl => new($"http://{_vm.Hostname}:{_vm.GetMappedPublicPort(8428)}/");

    [Fact]
    public async Task Samples_in_model_time_are_imported_and_read_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = new VictoriaMetricsWriter(Configured(BaseUrl.ToString()), new OneClient(_http), NullLogger<VictoriaMetricsWriter>.Instance);
        var series = PrometheusText.Series(HistorianMetrics.OverflowTotal, ("run", "r1"), ("storm", "2000-05-30"), ("algorithm", "efd-center"));

        writer.Write([.. Enumerable.Range(0, 4).Select(k => new Sample(series, 10.0 * k, StormStartMs + k * 300_000L))]);
        await writer.Writer.FlushAsync(ct);
        Assert.Equal(4, writer.Writer.Written);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement exported = default;
        while (DateTime.UtcNow < deadline)
        {
            var text = await _http.GetStringAsync(new Uri(BaseUrl, $"api/v1/export?match[]={HistorianMetrics.OverflowTotal}"), ct);
            if (text.Length > 0)
            {
                exported = JsonDocument.Parse(text).RootElement;
                break;
            }
            await Task.Delay(250, ct);
        }

        Assert.Equal(JsonValueKind.Object, exported.ValueKind);
        Assert.Equal("r1", exported.GetProperty("metric").GetProperty("run").GetString());
        Assert.Equal([0.0, 10.0, 20.0, 30.0], exported.GetProperty("values").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal(StormStartMs + 900_000L, exported.GetProperty("timestamps")[3].GetInt64());

        var range = await _http.GetFromJsonAsync<JsonElement>(
            new Uri(BaseUrl, $"api/v1/query_range?query={HistorianMetrics.OverflowTotal}&start={StormStartMs / 1000}&end={StormStartMs / 1000 + 900}&step=300"), ct);
        Assert.Equal(4, range.GetProperty("data").GetProperty("result")[0].GetProperty("values").GetArrayLength());
    }

    [Fact]
    public async Task An_unreachable_VictoriaMetrics_loses_the_batch_but_does_not_throw()
    {
        var writer = new VictoriaMetricsWriter(Configured("http://127.0.0.1:9"), new OneClient(_http), NullLogger<VictoriaMetricsWriter>.Instance);

        writer.Write([new Sample("m", 1, StormStartMs)]);
        await writer.Writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, writer.Writer.Failed);
    }

    private static IOptions<VictoriaMetricsOptions> Configured(string url) => Options.Create(new VictoriaMetricsOptions { Url = url });

    private sealed class OneClient(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
