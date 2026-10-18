using StormBench.Application.Alarms;
using StormBench.Application.Site;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Runs;

/// <summary>
/// Follows the runs of node Sim01 and keeps a summary of each: saved when the run starts and
/// again when it ends (finished, stopped, failed). A run that ends unseen (the simulator
/// restarts, or the next run begins) is closed as interrupted. <see cref="Current"/> is the open
/// run, or the last one, so that alarms acknowledged after the end still belong to it.
/// </summary>
public sealed class RunRecorder(IRunStore store, TimeProvider clock) : ICurrentRun
{
    private readonly Lock _gate = new();
    private long _simRun = -1;
    private RunSummary? _current;
    private bool _open;

    public RunSummary? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public RunId? CurrentRunId => Current?.Id;

    /// <summary>Whether <see cref="Current"/> is still running.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _open;
            }
        }
    }

    public void Observe(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var field = site.FieldValues;
        if (site.Sim is not Presence.Online || field.Run is not { } run)
        {
            return;
        }
        lock (_gate)
        {
            if (run != _simRun)
            {
                _simRun = run;
                if (_open)
                {
                    Close(RunState.Interrupted);
                }
                if (run > 0 && field.State.IsBusy)
                {
                    Begin(site, run);
                }
                return;
            }
            if (!_open)
            {
                return;
            }
            _current = Progress(_current!, field);
            if (RunSummary.Ended(field.State) is { } ended)
            {
                Close(ended);
            }
        }
    }

    private void Begin(SiteSnapshot site, long run)
    {
        var field = site.FieldValues;
        var begun = RunSummary.Begin(
            run, field.Storm ?? new StormId(""), field.Algorithm ?? new AlgorithmId(""), field.Pace, site.Catalog, clock.GetUtcNow());
        _current = Progress(begun, field);
        _open = true;
        store.Save(_current);
    }

    private void Close(RunState state)
    {
        _current = _current!.Close(state, clock.GetUtcNow());
        _open = false;
        store.Save(_current);
    }

    private static RunSummary Progress(RunSummary run, IFieldReadings field) =>
        run.Progress(field.CurrentStep, field.Steps, field.OverflowTotal, field.BaseCaseOverflowTotal, field.WwtpInflow, field.Mismatches);
}
