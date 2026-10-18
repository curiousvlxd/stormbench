using StormBench.Application.Kpi;
using StormBench.Contracts;
using StormBench.Domain.Kpi;
using StormBench.Domain.Network;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

public class OverflowKpiTests
{
    private readonly SiteFixture _site = new();

    private OverflowKpi Kpi(double total, double? baseCase, params string[] overflowing)
    {
        var metrics = new List<SparkplugMetric>
        {
            SparkplugMetric.Int64(SimMetrics.Run, 1),
            SparkplugMetric.Double(SimMetrics.OverflowTotal, total),
            SparkplugMetric.Double(SimMetrics.WwtpInflow, 48_976.7),
        };
        if (baseCase is { } b)
        {
            metrics.Add(SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, b));
        }
        foreach (var point in new[] { "T1", "T2", "CSO1" })
        {
            metrics.Add(SparkplugMetric.Boolean(SimMetrics.OverflowActive(point), overflowing.Contains(point)));
        }
        _site.SimBirth([.. metrics]);
        return SiteKpi.Overflow(_site.Read());
    }

    [Fact]
    public void Less_overflow_than_the_base_case_is_better()
    {
        var kpi = Kpi(12_069.95, 13_475.86, "T1");

        Assert.Equal(Comparison.Better, kpi.Comparison);
        Assert.Equal(-1_405.91, kpi.Delta!.Value.CubicMeters, 2);
        Assert.Equal(-0.1043, kpi.Relative!.Value, 4);
        Assert.Equal([new OverflowPointId("T1")], kpi.Overflowing);
        Assert.Equal(3, kpi.Points);
        Assert.Equal(new Volume(48_976.7), kpi.WwtpInflow);
    }

    [Fact]
    public void Within_one_percent_is_the_same()
    {
        Assert.Equal(Comparison.Same, Kpi(1_005, 1_000).Comparison);
        Assert.Equal(Comparison.Same, Kpi(995.5, 1_000).Comparison);
    }

    [Fact]
    public void More_overflow_than_the_base_case_is_worse()
    {
        Assert.Equal(Comparison.Worse, Kpi(1_020, 1_000).Comparison);
    }

    [Fact]
    public void No_overflow_in_either_is_the_same_and_any_overflow_against_none_is_worse()
    {
        Assert.Equal(Comparison.Same, Kpi(0.2, 0.0).Comparison);

        var kpi = Kpi(40, 0.0);
        Assert.Equal(Comparison.Worse, kpi.Comparison);
        Assert.Null(kpi.Relative);
    }

    [Fact]
    public void Before_the_first_run_there_is_nothing_to_compare()
    {
        _site.SimBirth();

        Assert.Equal(Comparison.NoData, SiteKpi.Overflow(_site.Read()).Comparison);
    }

    [Fact]
    public void Missing_reference_is_no_comparison()
    {
        Assert.Equal(Comparison.NoData, Kpi(500, null).Comparison);
    }
}
