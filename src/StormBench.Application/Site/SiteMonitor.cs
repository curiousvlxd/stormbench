using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Runs;

namespace StormBench.Application.Site;

/// <summary>
/// Evaluates the site after every UNS message and once a second without traffic: the run
/// history first, then the alarm conditions, the trend of the current run and its samples.
/// </summary>
public sealed class SiteMonitor(
    ISiteReader site, RunRecorder runs, AlarmEngine alarms, FieldChecks fieldChecks, RunTrend trend, RunSampler samples)
{
    public void Evaluate()
    {
        var snapshot = site.Read();
        runs.Observe(snapshot);
        alarms.Apply(SiteConditions.Evaluate(snapshot).Concat(fieldChecks.Evaluate(snapshot)), snapshot.SimClock);
        trend.Observe(snapshot);
        samples.Observe(snapshot);
    }
}
