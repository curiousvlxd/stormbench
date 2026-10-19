using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>Run states as the column <c>runs.state</c> keeps them.</summary>
public static class RunStateText
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";

    public static string Of(RunState state) => state switch
    {
        RunState.Running => Running,
        RunState.Finished => Finished,
        RunState.Stopped => Stopped,
        RunState.Failed => Failed,
        RunState.Interrupted => Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static RunState Parse(string state) => state switch
    {
        Running => RunState.Running,
        Finished => RunState.Finished,
        Stopped => RunState.Stopped,
        Failed => RunState.Failed,
        Interrupted => RunState.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}
