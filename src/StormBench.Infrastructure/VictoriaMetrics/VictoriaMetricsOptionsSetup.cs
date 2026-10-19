using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace StormBench.Infrastructure.VictoriaMetrics;

/// <summary>Binds <see cref="VictoriaMetricsOptions"/> to the section <c>VictoriaMetrics</c>.</summary>
internal sealed class VictoriaMetricsOptionsSetup(IConfiguration configuration) : IConfigureOptions<VictoriaMetricsOptions>
{
    public void Configure(VictoriaMetricsOptions options) => configuration.GetSection(VictoriaMetricsOptions.Section).Bind(options);
}
