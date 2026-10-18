using StormBench.Infrastructure.VictoriaMetrics;

namespace StormBench.Scada.Tests;

/// <summary>The lines the historian posts to VictoriaMetrics' Prometheus import.</summary>
public class PrometheusTextTests
{
    [Fact]
    public void A_sample_is_name_labels_value_and_milliseconds()
    {
        var series = PrometheusText.Series(HistorianMetrics.TankLevel, ("run", "20261017-142530-3"), ("storm", "2000-05-30"), ("tank", "T2"));
        var text = PrometheusText.Format([new Sample(series, 1.25, 959_644_800_000)]);

        Assert.Equal("stormbench_tank_level_meters{run=\"20261017-142530-3\",storm=\"2000-05-30\",tank=\"T2\"} 1.25 959644800000\n", text);
    }

    [Fact]
    public void Label_values_are_escaped()
    {
        Assert.Equal("m{a=\"x\\\\y\\\"z\\n\"}", PrometheusText.Series("m", ("a", "x\\y\"z\n")));
        Assert.Equal("m", PrometheusText.Series("m"));
    }

    [Fact]
    public void Values_keep_every_digit()
    {
        var text = PrometheusText.Format([new Sample("m", 12069.954999999998, 1), new Sample("m", 1e-7, 2), new Sample("m", -3, 3)]);
        Assert.Equal("m 12069.954999999998 1\nm 1E-07 2\nm -3 3\n", text);
    }

    [Fact]
    public void Values_that_are_not_finite_are_left_out()
    {
        var text = PrometheusText.Format([new Sample("m", double.NaN, 1), new Sample("m", double.PositiveInfinity, 2), new Sample("m", 0, 3)]);
        Assert.Equal("m 0 3\n", text);
    }
}
