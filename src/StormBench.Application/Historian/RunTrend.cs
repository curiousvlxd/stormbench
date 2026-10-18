using StormBench.Application.Site;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Historian;

/// <summary>One control step of the running storm: rain and overflow totals since the run started.</summary>
public sealed record TrendPoint(long Step, DateTime Time, double Rain, double Overflow, double? BaseCase);

/// <summary>The trend of one run, reduced to at most a given number of points for drawing.</summary>
public sealed record TrendSeries(long Run, StormId? Storm, IReadOnlyList<TrendPoint> Points)
{
    public static TrendSeries Empty { get; } = new(0, null, []);
}

/// <summary>
/// In-memory history of the current storm, one point per control step, fed by node Sim01. A new
/// run starts a new trend. The long-term historian is VictoriaMetrics; this one only serves the
/// HMI trend of the running storm.
/// </summary>
public sealed class RunTrend
{
    private readonly Lock _gate = new();
    private readonly List<TrendPoint> _points = [];
    private long _run = -1;
    private StormId? _storm;
    private long _lastStep;
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public void Observe(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var field = site.FieldValues;
        if (site.Sim is not Presence.Online || field.Run is not { } run || run <= 0)
        {
            return;
        }
        lock (_gate)
        {
            if (run != _run)
            {
                _run = run;
                _storm = field.Storm;
                _points.Clear();
                _lastStep = 0;
                Interlocked.Increment(ref _version);
                return;
            }
            if (field.CurrentStep is not { } step || step <= _lastStep || site.SimClock is not { } time)
            {
                return;
            }
            _lastStep = step;
            _points.Add(new TrendPoint(
                step,
                time,
                field.Rain ?? 0.0,
                field.OverflowTotal?.CubicMeters ?? 0.0,
                field.BaseCaseOverflowTotal?.CubicMeters));
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// The current run with at most <paramref name="maxPoints"/> points: steps are grouped into
    /// equal buckets, keeping the heaviest rain of a bucket and its last (cumulative) totals.
    /// </summary>
    public TrendSeries Read(int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, 1);
        lock (_gate)
        {
            if (_run <= 0)
            {
                return TrendSeries.Empty;
            }
            if (_points.Count <= maxPoints)
            {
                return new TrendSeries(_run, _storm, [.. _points]);
            }
            var bucket = (int)Math.Ceiling(_points.Count / (double)maxPoints);
            var reduced = new List<TrendPoint>(maxPoints);
            for (var start = 0; start < _points.Count; start += bucket)
            {
                var end = Math.Min(start + bucket, _points.Count);
                var rain = 0.0;
                for (var i = start; i < end; i++)
                {
                    rain = Math.Max(rain, _points[i].Rain);
                }
                reduced.Add(_points[end - 1] with { Rain = rain });
            }
            return new TrendSeries(_run, _storm, reduced);
        }
    }
}
