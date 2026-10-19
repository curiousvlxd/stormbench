using Microsoft.Extensions.Options;

namespace StormBench.Connectivity.Plc;

/// <summary>Binds <see cref="PlcOptions"/> to the section <c>Plc</c>.</summary>
internal sealed class PlcOptionsSetup(IConfiguration configuration) : IConfigureOptions<PlcOptions>
{
    public void Configure(PlcOptions options) => configuration.GetSection(PlcOptions.Section).Bind(options);
}
