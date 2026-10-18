namespace StormBench.Domain.Alarms;

/// <summary>ISA-18.2 priorities as the HMI shows them: 1 high, 2 medium, 3 low.</summary>
public enum AlarmPriority
{
    High = 1,
    Medium = 2,
    Low = 3
}

/// <summary>ISA-18.2 alarm states (shelving, suppression and out of service are not modelled yet).</summary>
public enum AlarmState
{
    Normal,

    /// <summary>Active, not acknowledged.</summary>
    Unacknowledged,

    /// <summary>Active, acknowledged.</summary>
    Acknowledged,

    /// <summary>Returned to normal before anyone acknowledged it.</summary>
    ReturnedUnacknowledged
}

public sealed record AlarmDefinition(string Id, string Source, string Message, AlarmPriority Priority);

/// <summary>
/// One ISA-18.2 state change of an alarm. SimTime is the simulated time of the data that caused
/// it; for an acknowledgement, the simulated time last seen.
/// </summary>
public sealed record AlarmTransition(AlarmDefinition Definition, AlarmState From, AlarmState To, DateTimeOffset WallTime, DateTime? SimTime);

/// <summary>
/// An alarm that needs the operator's attention, with its ISA-18.2 lifecycle: it becomes
/// Unacknowledged when its condition turns true; the operator acknowledges it; it returns to
/// Normal only when the condition is false and it has been acknowledged. A condition that clears
/// first leaves it ReturnedUnacknowledged, so no alarm passes unseen. SimTime is the simulated
/// time when it became active.
/// </summary>
public sealed record Alarm(
    AlarmDefinition Definition,
    AlarmState State,
    DateTimeOffset ActivatedAt,
    DateTime? SimTime,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ReturnedAt)
{
    public bool Active => State is AlarmState.Unacknowledged or AlarmState.Acknowledged;

    public bool NeedsAcknowledge => State is AlarmState.Unacknowledged or AlarmState.ReturnedUnacknowledged;

    /// <summary>The state after the condition was evaluated as active or not; unchanged when nothing happens.</summary>
    public static AlarmState Next(AlarmState state, bool conditionActive) => (state, conditionActive) switch
    {
        (AlarmState.Normal, true) => AlarmState.Unacknowledged,
        (AlarmState.ReturnedUnacknowledged, true) => AlarmState.Unacknowledged,
        (AlarmState.Unacknowledged, false) => AlarmState.ReturnedUnacknowledged,
        (AlarmState.Acknowledged, false) => AlarmState.Normal,
        _ => state
    };

    /// <summary>The state after the operator acknowledged; unchanged when there is nothing to acknowledge.</summary>
    public static AlarmState AfterAcknowledge(AlarmState state) => state switch
    {
        AlarmState.Unacknowledged => AlarmState.Acknowledged,
        AlarmState.ReturnedUnacknowledged => AlarmState.Normal,
        _ => state
    };

    public static Alarm Raise(AlarmDefinition definition, DateTimeOffset now, DateTime? simTime) =>
        new(definition, AlarmState.Unacknowledged, now, simTime, null, null);
}
