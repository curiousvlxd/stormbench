using StormBench.Application.Alarms;
using StormBench.Application.Runs;
using StormBench.Application.Site;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Historian;

/// <summary>
/// The values of one control step of a run, stamped with the simulated time of the step.
/// Volumes are cumulative since the start of the run; the PLC's values are left out while the
/// PLC is not online, and every value is left out when it is unknown.
/// </summary>
public sealed record RunStepSample
{
    public required RunSummary Run { get; init; }

    public required long Step { get; init; }

    /// <summary>Simulated time of the step as Unix milliseconds.</summary>
    public required long TimestampMs { get; init; }

    public double? Rain { get; init; }

    public Volume? OverflowTotal { get; init; }

    public Volume? BaseCaseTotal { get; init; }

    public Volume? WwtpInflow { get; init; }

    public long? Mismatches { get; init; }

    public IReadOnlyList<(OverflowPointId Point, Volume? Volume)> Overflow { get; init; } = [];

    public IReadOnlyList<(TankId Tank, double? Level)> TankLevels { get; init; } = [];

    public IReadOnlyList<(GateId Gate, GateOpening? Setpoint)> Setpoints { get; init; } = [];

    public long? Mode { get; init; }

    public required int AlarmsActive { get; init; }

    public required int AlarmsUnacknowledged { get; init; }
}

/// <summary>Keeps the steps of the runs as time series. Must not block: it is called on the UNS host's path.</summary>
public interface IRunSeriesWriter
{
    void Write(RunStepSample sample);
}

/// <summary>
/// Turns every control step of the current run into a sample, stamped with the simulated time
/// of the step (ADR 0015), so a run is a time series over its storm window and runs of the same
/// storm lie on top of each other. The values of a step come from Sim01's report of it and the
/// PLC's last values; when the run finishes, one more sample at the end of the window carries the
/// final totals.
/// </summary>
public sealed class RunSampler(RunRecorder runs, AlarmEngine alarms, IRunSeriesWriter series)
{
    private readonly Lock _gate = new();
    private RunId? _run;
    private long _lastStep;
    private long _lastClockMs;

    public void Observe(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var field = site.FieldValues;
        if (site.Sim is not Presence.Online
            || runs.Current is not { } run
            || field.Run != run.SimRun
            || field.Clock is not { } clock)
        {
            return;
        }
        var clockMs = (clock.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
        var step = field.CurrentStep ?? 0;
        RunStepSample sample;
        lock (_gate)
        {
            if (_run != run.Id)
            {
                _run = run.Id;
                _lastStep = 0;
                _lastClockMs = 0;
            }
            var newStep = field.State is ScenarioState.Running && step > _lastStep;
            var finalSet = field.State is ScenarioState.Finished && _lastStep > 0 && clockMs > _lastClockMs;
            if (!newStep && !finalSet)
            {
                return;
            }
            _lastStep = Math.Max(_lastStep, step);
            _lastClockMs = clockMs;
            sample = Collect(run, site, step, clockMs);
        }
        series.Write(sample);
    }

    private RunStepSample Collect(RunSummary run, SiteSnapshot site, long step, long clockMs)
    {
        var field = site.FieldValues;
        var plc = site.PlcValues;
        var plcOnline = site.Plc is Presence.Online;
        var current = alarms.Current();
        return new RunStepSample
        {
            Run = run,
            Step = step,
            TimestampMs = clockMs,
            Rain = field.Rain,
            OverflowTotal = field.OverflowTotal,
            BaseCaseTotal = field.BaseCaseOverflowTotal,
            WwtpInflow = field.WwtpInflow,
            Mismatches = field.Mismatches,
            Overflow = [.. (site.Network?.OverflowPoints ?? []).Select(p => (p.Id, field.OverflowVolume(p.Id)))],
            TankLevels = plcOnline ? [.. (site.Network?.Tanks ?? plc.Tanks).Select(t => (t, Good(plc.Level(t))))] : [],
            Setpoints = plcOnline ? [.. plc.Stations.Select(s => (s.Gate, Good(plc.Setpoint(s.Gate))))] : [],
            Mode = plcOnline && plc.ModeActive is { Good: true } mode ? (long)mode.Value : null,
            AlarmsActive = current.Count(a => a.Active),
            AlarmsUnacknowledged = current.Count(a => a.NeedsAcknowledge),
        };
    }

    private static T? Good<T>(Reading<T>? reading)
        where T : struct => reading is { Good: true } r ? r.Value : null;
}
