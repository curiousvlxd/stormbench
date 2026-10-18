using StormBench.Infrastructure.VictoriaMetrics;

namespace StormBench.Scada.Tests;

/// <summary>The VictoriaMetrics base URL resolves the import path below it, with or without a trailing slash.</summary>
public class VictoriaMetricsOptionsTests
{
    [Theory]
    [InlineData("http://vm:8428", "http://vm:8428/")]
    [InlineData("http://vm:8428/", "http://vm:8428/")]
    [InlineData("http://vm:8428/prefix", "http://vm:8428/prefix/")]
    public void The_url_keeps_a_trailing_slash(string configured, string expected)
    {
        var options = new VictoriaMetricsOptions { Url = configured };

        Assert.True(options.Enabled);
        Assert.Equal(new Uri(expected), options.BaseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_url_the_historian_is_off(string? configured) =>
        Assert.False(new VictoriaMetricsOptions { Url = configured }.Enabled);
}
