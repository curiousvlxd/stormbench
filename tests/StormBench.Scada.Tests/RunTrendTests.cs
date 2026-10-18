using StormBench.Application.Historian;
using StormBench.Contracts;
using StormBench.Domain.Scenarios;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

public class RunTrendTests
{
    private static readonly DateTime Start = new(2000, 5, 30, 0, 0, 0);
    private readonly SiteFixture _site = new();
    private readonly RunTrend _trend = new();

    private void Step(long step, double rain, double overflow, double baseCase)
    {
        _site.SimData(
            SparkplugMetric.Int64(SimMetrics.Step, step),
            SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(Start.AddMinutes(5 * step))),
            SparkplugMetric.Double(SimMetrics.RainIntensity, rain),
            SparkplugMetric.Double(SimMetrics.OverflowTotal, overflow),
            SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, baseCase));
        _trend.Observe(_site.Read());
    }

    private void NewRun(long run)
    {
        _site.SimData(SparkplugMetric.Int64(SimMetrics.Run, run), SparkplugMetric.String(SimMetrics.Storm, "s1"));
        _trend.Observe(_site.Read());
    }

    [Fact]
    public void Records_one_point_per_new_step()
    {
        _site.SimBirth();
        NewRun(1);
        Step(1, 2.0, 0, 0);
        Step(2, 4.0, 10, 12);
        _trend.Observe(_site.Read());

        var series = _trend.Read(100);
        Assert.Equal(1, series.Run);
        Assert.Equal(new StormId("s1"), series.Storm);
        Assert.Equal([1L, 2L], series.Points.Select(p => p.Step));
        Assert.Equal(Start.AddMinutes(10), series.Points[1].Time);
        Assert.Equal(12, series.Points[1].BaseCase);
    }

    [Fact]
    public void A_new_run_starts_a_new_trend()
    {
        _site.SimBirth();
        NewRun(1);
        Step(1, 2.0, 5, 5);
        NewRun(2);
        Step(1, 1.0, 0, 0);

        var series = _trend.Read(100);
        Assert.Equal(2, series.Run);
        Assert.Single(series.Points);
    }

    [Fact]
    public void Reduction_keeps_the_heaviest_rain_and_the_last_totals_of_a_bucket()
    {
        _site.SimBirth();
        NewRun(1);
        for (var k = 1; k <= 10; k++)
        {
            Step(k, k == 3 ? 30.0 : 1.0, k * 10, k * 11);
        }

        var series = _trend.Read(5);

        Assert.Equal(5, series.Points.Count);
        Assert.Equal(30.0, series.Points[1].Rain);
        Assert.Equal(40, series.Points[1].Overflow);
        Assert.Equal(100, series.Points[^1].Overflow);
    }

    [Fact]
    public void Nothing_is_recorded_before_the_first_run_or_from_a_dead_simulator()
    {
        _site.SimBirth();
        Step(1, 1.0, 0, 0);
        Assert.Empty(_trend.Read(10).Points);

        NewRun(1);
        _site.SimDeath();
        _trend.Observe(_site.Read());
        Assert.Empty(_trend.Read(10).Points);
    }
}
