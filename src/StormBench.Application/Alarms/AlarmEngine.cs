using StormBench.Domain.Alarms;

namespace StormBench.Application.Alarms;

/// <summary>Is told every state change, in order, under the engine's lock: it must only queue, never block.</summary>
public interface IAlarmObserver
{
    void OnTransition(AlarmTransition transition);
}

/// <summary>
/// The site's <see cref="AlarmBoard"/> shared by the UNS host and the displays: one lock around
/// it, a version for the displays, and every state change to the observers (the alarm journal).
/// </summary>
public sealed class AlarmEngine(TimeProvider clock, IEnumerable<IAlarmObserver>? observers = null)
{
    private readonly Lock _gate = new();
    private readonly AlarmBoard _board = new();
    private readonly IAlarmObserver[] _observers = observers?.ToArray() ?? [];
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    /// <summary>Applies the current value of conditions. Conditions left out (no good data) keep their state.</summary>
    public void Apply(IEnumerable<(AlarmDefinition Definition, bool Active)> conditions, DateTime? simTime = null)
    {
        lock (_gate)
        {
            Notify(_board.Apply(conditions, clock.GetUtcNow(), simTime));
        }
    }

    public bool Acknowledge(string id)
    {
        lock (_gate)
        {
            if (_board.Acknowledge(id, clock.GetUtcNow()) is not { } transition)
            {
                return false;
            }
            Notify([transition]);
            return true;
        }
    }

    public int AcknowledgeAll()
    {
        lock (_gate)
        {
            var transitions = _board.AcknowledgeAll(clock.GetUtcNow());
            Notify(transitions);
            return transitions.Count;
        }
    }

    /// <summary>Alarms that are active or wait for acknowledgement: highest priority first, then newest.</summary>
    public IReadOnlyList<Alarm> Current()
    {
        lock (_gate)
        {
            return _board.Current();
        }
    }

    private void Notify(IReadOnlyList<AlarmTransition> transitions)
    {
        if (transitions.Count is 0)
        {
            return;
        }
        Interlocked.Add(ref _version, transitions.Count);
        foreach (var transition in transitions)
        {
            foreach (var observer in _observers)
            {
                observer.OnTransition(transition);
            }
        }
    }
}

/// <summary>The operator acknowledges one alarm.</summary>
public sealed record AcknowledgeAlarm(string Id);

/// <summary>False when the alarm does not exist or does not wait for an acknowledgement.</summary>
public sealed class AcknowledgeAlarmHandler(AlarmEngine alarms)
{
    public bool Handle(AcknowledgeAlarm command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return alarms.Acknowledge(command.Id);
    }

    /// <summary>Acknowledges every alarm that waits for it; the number acknowledged.</summary>
    public int HandleAll() => alarms.AcknowledgeAll();
}
