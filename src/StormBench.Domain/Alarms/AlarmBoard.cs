namespace StormBench.Domain.Alarms;

/// <summary>
/// The alarms of the site, one per definition, as an aggregate: conditions and acknowledgements
/// move each alarm through its lifecycle and every state change comes back as a transition. An
/// alarm in state Normal leaves the board. Not thread safe: its owner serialises the calls.
/// </summary>
public sealed class AlarmBoard
{
    private readonly Dictionary<string, Alarm> _alarms = [];

    /// <summary>Simulated time of the latest evaluated data, stamped on acknowledgements.</summary>
    public DateTime? LastSimTime { get; private set; }

    /// <summary>Applies the current value of conditions. Conditions left out (no good data) keep their state.</summary>
    public IReadOnlyList<AlarmTransition> Apply(IEnumerable<(AlarmDefinition Definition, bool Active)> conditions, DateTimeOffset now, DateTime? simTime)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        LastSimTime = simTime ?? LastSimTime;
        List<AlarmTransition> transitions = [];
        foreach (var (definition, active) in conditions)
        {
            var current = _alarms.GetValueOrDefault(definition.Id);
            var state = current?.State ?? AlarmState.Normal;
            var next = Alarm.Next(state, active);
            if (next == state)
            {
                continue;
            }
            Set(definition.Id, next switch
            {
                AlarmState.Unacknowledged => Alarm.Raise(definition, now, simTime),
                AlarmState.ReturnedUnacknowledged => current! with { State = next, ReturnedAt = now },
                _ => null,
            });
            transitions.Add(new AlarmTransition(definition, state, next, now, simTime));
        }
        return transitions;
    }

    /// <summary>The operator acknowledges one alarm; null when it does not wait for that.</summary>
    public AlarmTransition? Acknowledge(string id, DateTimeOffset now)
    {
        if (!_alarms.TryGetValue(id, out var alarm))
        {
            return null;
        }
        var next = Alarm.AfterAcknowledge(alarm.State);
        if (next == alarm.State)
        {
            return null;
        }
        Set(id, next is AlarmState.Normal ? null : alarm with { State = next, AcknowledgedAt = now });
        return new AlarmTransition(alarm.Definition, alarm.State, next, now, LastSimTime);
    }

    public IReadOnlyList<AlarmTransition> AcknowledgeAll(DateTimeOffset now) =>
        [.. _alarms.Keys.ToList().Select(id => Acknowledge(id, now)).OfType<AlarmTransition>()];

    /// <summary>Alarms that are active or wait for acknowledgement: highest priority first, then newest.</summary>
    public IReadOnlyList<Alarm> Current() =>
    [
        .. _alarms.Values
            .OrderBy(a => a.Definition.Priority)
            .ThenByDescending(a => a.ActivatedAt)
            .ThenBy(a => a.Definition.Id, StringComparer.Ordinal),
    ];

    private void Set(string id, Alarm? alarm)
    {
        if (alarm is null)
        {
            _alarms.Remove(id);
        }
        else
        {
            _alarms[id] = alarm;
        }
    }
}
