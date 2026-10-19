using StormBench.Contracts;
using StormBench.Sparkplug.Payloads;

namespace StormBench.Scada.Tests;

/// <summary>A run of the test console as Sim01 reports it, for the historian tests.</summary>
internal static class RunScript
{
    /// <summary>Start of storm s1 in the fixture's catalog.</summary>
    public static readonly DateTime Start = new(2000, 5, 30, 0, 0, 0);

    public static DateTime At(long step) => Start.AddMinutes(5 * step);

    /// <summary>Start command accepted: run number, storm and algorithm with the telemetry reset.</summary>
    public static void BeginRun(this SiteFixture site, long run, string storm = "s1", string algorithm = "efd-center") =>
        site.SimData(
            SparkplugMetric.Int64(SimMetrics.Run, run),
            SparkplugMetric.String(SimMetrics.Storm, storm),
            SparkplugMetric.String(SimMetrics.Algorithm, algorithm),
            SparkplugMetric.Double(SimMetrics.Pace, 0.1),
            SparkplugMetric.Int64(SimMetrics.Step, 0),
            SparkplugMetric.Int64(SimMetrics.Steps, 431),
            SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(Start)),
            SparkplugMetric.Double(SimMetrics.OverflowTotal, 0.0),
            SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, 0.0),
            SparkplugMetric.Double(SimMetrics.WwtpInflow, 0.0),
            SparkplugMetric.Int64(SimMetrics.Mismatches, 0),
            SparkplugMetric.String(SimMetrics.State, ScenarioStates.Preparing));

    /// <summary>The Base Case of the window is computed, the live run starts.</summary>
    public static void Running(this SiteFixture site) =>
        site.SimData(SparkplugMetric.String(SimMetrics.State, ScenarioStates.Running));

    /// <summary>Sim01's report of one control step; CSO1 spills the whole overflow.</summary>
    public static void Step(this SiteFixture site, long step, double overflow, double baseCase, double rain = 1.0, long mismatches = 0) =>
        site.SimData(
            SparkplugMetric.Int64(SimMetrics.Step, step),
            SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(At(step))),
            SparkplugMetric.Double(SimMetrics.RainIntensity, rain),
            SparkplugMetric.Double(SimMetrics.OverflowTotal, overflow),
            SparkplugMetric.Double(SimMetrics.OverflowVolume("CSO1"), overflow),
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("CSO1"), overflow > 0),
            SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, baseCase),
            SparkplugMetric.Double(SimMetrics.WwtpInflow, 100.0 * step),
            SparkplugMetric.Int64(SimMetrics.Mismatches, mismatches));

    /// <summary>The run ends; a finished run reports its totals at the end of the window.</summary>
    public static void EndRun(this SiteFixture site, string state, double? overflow = null, double? baseCase = null, DateTime? clock = null)
    {
        var metrics = new List<SparkplugMetric>();
        if (clock is { } c)
        {
            metrics.Add(SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(c)));
        }
        if (overflow is { } o)
        {
            metrics.Add(SparkplugMetric.Double(SimMetrics.OverflowTotal, o));
            metrics.Add(SparkplugMetric.Double(SimMetrics.OverflowVolume("CSO1"), o));
        }
        if (baseCase is { } b)
        {
            metrics.Add(SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, b));
        }
        metrics.Add(SparkplugMetric.String(SimMetrics.State, state));
        site.SimData([.. metrics]);
    }
}
