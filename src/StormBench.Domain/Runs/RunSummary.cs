using System.Diagnostics.CodeAnalysis;
using StormBench.Domain.Kpi;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;

namespace StormBench.Domain.Runs;

/// <summary>
/// A run of the test console, unique across simulator restarts (the simulator counts its runs
/// from 1 again): the wall time it started and the simulator's run number, e.g. 20261017-142530-3.
/// </summary>
public readonly record struct RunId(string Value) : IParsable<RunId>
{
    public static RunId For(DateTimeOffset startedAt, long simRun) => new($"{startedAt.UtcDateTime:yyyyMMdd-HHmmss}-{simRun}");

    public override string ToString() => Value;

    public static RunId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out RunId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}

public enum RunState
{
    Running,
    Finished,
    Stopped,
    Failed,

    /// <summary>The run ended without Scada seeing its end (simulator or Scada restarted).</summary>
    Interrupted
}

/// <summary>
/// One run of the test console, as it is compared with others: what ran, how far it got and how
/// much it spilled against the Base Case of the same storm window. Storm times are model time,
/// StartedAt and FinishedAt wall time.
/// </summary>
public sealed record RunSummary(
    RunId Id,
    long SimRun,
    StormId Storm,
    string? StormLabel,
    AlgorithmId Algorithm,
    string? AlgorithmLabel,
    double? PaceSeconds,
    RunState State,
    DateTime? StormStart,
    DateTime? StormEnd,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long Steps,
    long StepsPlanned,
    Volume Overflow,
    Volume? BaseCase,
    double? ChangePercent,
    Volume? WwtpInflow,
    long Mismatches)
{
    /// <summary>A run the simulator has taken up: nothing done and nothing spilled yet.</summary>
    public static RunSummary Begin(
        long simRun, StormId storm, AlgorithmId algorithm, double? paceSeconds, ScenarioCatalog? catalog, DateTimeOffset startedAt)
    {
        var window = catalog?.Storm(storm);
        return new RunSummary(
            RunId.For(startedAt, simRun),
            simRun,
            storm,
            window?.Label,
            algorithm,
            catalog?.Algorithm(algorithm)?.Label,
            paceSeconds,
            RunState.Running,
            window?.Start,
            window?.End,
            startedAt,
            null,
            0,
            0,
            Volume.Zero,
            null,
            null,
            null,
            0);
    }

    /// <summary>The run's figures after a report of the field; what the report leaves out keeps its value.</summary>
    public RunSummary Progress(long? steps, long? stepsPlanned, Volume? overflow, Volume? baseCase, Volume? wwtpInflow, long? mismatches)
    {
        var total = overflow ?? Overflow;
        var reference = baseCase ?? BaseCase;
        return this with
        {
            Steps = steps ?? Steps,
            StepsPlanned = stepsPlanned ?? StepsPlanned,
            Overflow = total,
            BaseCase = reference,
            ChangePercent = Change(total, reference),
            WwtpInflow = wwtpInflow ?? WwtpInflow,
            Mismatches = mismatches ?? Mismatches
        };
    }

    public RunSummary Close(RunState state, DateTimeOffset finishedAt) => this with { State = state, FinishedAt = finishedAt };

    /// <summary>The run state a storm ends in; null while the storm is not over.</summary>
    public static RunState? Ended(ScenarioState state) => state switch
    {
        ScenarioState.Finished => RunState.Finished,
        ScenarioState.Stopped => RunState.Stopped,
        ScenarioState.Failed => RunState.Failed,
        _ => null
    };

    /// <summary>
    /// Overflow against the Base Case in percent, null while the Base Case spills less than
    /// <see cref="OverflowKpi.NoOverflow"/> (a relative change of almost nothing says nothing).
    /// </summary>
    public static double? Change(Volume overflow, Volume? baseCase) =>
        baseCase is { } b && b >= OverflowKpi.NoOverflow ? overflow.RelativeTo(b) * 100.0 : null;
}
