using StormBench.Application.Scenarios;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;

namespace StormBench.Scada.Hmi.Live;

public enum DemoCommand
{
    CutLink,
    RestoreLink,
    JamGate,
    FreeGate,
    Overview,
    FocusCenter,
    FocusPlc,
    FocusGate
}

/// <summary>One chapter of the guided demo, shown large over the scene.</summary>
public sealed record DemoChapter(int Number, int Of, string Title, string Subtitle);

/// <summary>
/// The guided demo: one storm with smart control from the control room, the link to the PLC
/// cut during the first rain, a gate jammed before the downpour, then the verdict. Cues are
/// tied to control steps of the storm of 30 May 2000 (5 min each, from 00:00), so the faults
/// land while something happens: the first overflow around 06:40, the downpour from 22:00.
/// The page feeds it the scenario status on every change and executes the commands it returns.
/// </summary>
public sealed class LiveDemo(long runBefore, GateId gate)
{
    public static StormId Storm { get; } = new("2000-05-30");

    public static AlgorithmId Algorithm { get; } = new("efd-center");

    public static AlgorithmId PredictiveAlgorithm { get; } = new("mpc-center");

    /// <summary>The demo runs predictive control from the center when the catalog offers it, else EFD from the center.</summary>
    public static AlgorithmId? PickAlgorithm(ScenarioCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Algorithm(PredictiveAlgorithm) is not null ? PredictiveAlgorithm
            : catalog.Algorithm(Algorithm) is not null ? Algorithm
            : null;
    }

    public const double PaceSeconds = 0.2;
    public const int Chapters = 4;

    /// <summary>NeedsGate: wait (until HoldUntil) for a gate the controller has moved away from its base setting.</summary>
    private sealed record Cue(long AtStep, DemoCommand[] Commands, Func<bool, DemoChapter?>? Chapter, bool NeedsGate = false, long HoldUntil = 0);

    private static readonly Cue[] Script =
    [
        new(0, [DemoCommand.Overview], _ => new(1, Chapters, "A storm hits the town", "Watch the tanks fill")),
        new(85, [DemoCommand.CutLink, DemoCommand.FocusCenter], _ => new(2, Chapters, "The control room goes dark", "The PLC carries on alone")),
        new(93, [DemoCommand.FocusPlc], null),
        new(140, [DemoCommand.RestoreLink, DemoCommand.Overview], _ => null),
        new(250, [DemoCommand.JamGate, DemoCommand.FocusGate], caught => new(3, Chapters, "A gate jams", caught ? "SCADA caught it" : "Will SCADA notice?"), NeedsGate: true, HoldUntil: 330),
        new(345, [DemoCommand.FreeGate, DemoCommand.Overview], _ => null),
    ];

    private int _next;
    private Func<bool, DemoChapter?>? _chapter;
    private long _run = -1;

    /// <summary>The gate the demo jams: the one the controller moved, else the first station's.</summary>
    public GateId Gate { get; private set; } = gate;

    /// <summary>The demo's storm has been taken up by the simulator.</summary>
    public bool Started => _run >= 0;

    public bool Done { get; private set; }

    /// <summary>Set when the storm has finished: the result line of the last chapter.</summary>
    public string? Verdict { get; private set; }

    public DemoChapter? Chapter(bool gateCaught) =>
        Verdict is { } verdict ? new(Chapters, Chapters, "The verdict", verdict) : _chapter?.Invoke(gateCaught);

    /// <summary>The commands due at this status; empty when nothing changes.</summary>
    /// <param name="movedGate">A controlled gate whose setpoint is away from its base setting now, or null.</param>
    public IReadOnlyList<DemoCommand> Advance(ScenarioStatus status, Func<string> verdict, Func<GateId?> movedGate)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(movedGate);
        if (Done)
        {
            return [];
        }
        if (!Started)
        {
            if (status.Run == runBefore || !status.Busy)
            {
                return [];
            }
            _run = status.Run;
        }
        if (status.Run != _run || status.State is ScenarioState.Stopped or ScenarioState.Failed)
        {
            Done = true;
            _chapter = null;
            return [DemoCommand.RestoreLink, DemoCommand.FreeGate];
        }
        if (status.State is ScenarioState.Finished)
        {
            Done = true;
            Verdict = verdict();
            return [DemoCommand.Overview];
        }
        List<DemoCommand> due = [];
        while (_next < Script.Length && status.Step >= Script[_next].AtStep)
        {
            var cue = Script[_next];
            if (cue.NeedsGate)
            {
                if (movedGate() is { } moved)
                {
                    Gate = moved;
                }
                else if (status.Step < cue.HoldUntil)
                {
                    break;
                }
            }
            _next++;
            due.AddRange(cue.Commands);
            if (cue.Chapter is not null)
            {
                _chapter = cue.Chapter;
            }
        }
        return due;
    }

    /// <summary>Ends the demo early (the viewer left it): the faults it set are cleared.</summary>
    public IReadOnlyList<DemoCommand> Abort()
    {
        if (Done)
        {
            return [];
        }
        Done = true;
        _chapter = null;
        return [DemoCommand.RestoreLink, DemoCommand.FreeGate];
    }
}
