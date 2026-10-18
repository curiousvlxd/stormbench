using StormBench.Application.Site;
using StormBench.Domain.Alarms;
using StormBench.Domain.Network;

namespace StormBench.Application.Alarms;

/// <summary>
/// Plausibility checks a SCADA runs on top of the PLC's own: they need a history, so they are
/// evaluated once per control step of the simulated field and must persist before they alarm.
/// - Gate deviation: a controlled outlet's position feedback differs from the PLC setpoint.
/// The field reports each step with the PLC step it belongs to; a step is checked once the PLC's
/// values of that same step are in the UNS, so the two reports never cross.
/// - Frozen level: a tank level reading has not changed for half an hour although the tank is
///   neither empty nor full (a healthy sensor in a storm moves every step).
/// </summary>
public sealed class FieldChecks
{
    /// <summary>Opening difference that counts as not following (0..1).</summary>
    public const double GateTolerance = 0.05;

    /// <summary>Control steps a deviation must last (15 min of model time).</summary>
    public const int GateSteps = 3;

    /// <summary>Control steps without any change of the reading (30 min of model time).</summary>
    public const int FrozenSteps = 6;

    /// <summary>Margin to empty and full within which an unchanged level is suspicious (m).</summary>
    public const double FrozenMargin = 0.05;

    private readonly Lock _gate = new();
    private readonly Dictionary<GateId, int> _gateCount = [];
    private readonly Dictionary<TankId, int> _frozenCount = [];
    private readonly Dictionary<TankId, double> _lastLevel = [];
    private long _run = -1;
    private long _step = -1;
    private long? _pendingPlcStep;

    public IEnumerable<(AlarmDefinition Definition, bool Active)> Evaluate(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (site.Sim is not Presence.Online || site.Plc is not Presence.Online || site.Network is not { } network)
        {
            return [];
        }
        var field = site.FieldValues;
        var run = field.Run ?? 0;
        var step = field.CurrentStep ?? 0;
        lock (_gate)
        {
            if (run != _run)
            {
                _run = run;
                _step = step;
                _gateCount.Clear();
                _frozenCount.Clear();
                _lastLevel.Clear();
                _pendingPlcStep = null;
            }
            else if (step > _step)
            {
                _step = step;
                _pendingPlcStep = field.PlcStep is > 0 and var plcStep ? plcStep : 0;
            }
            if (_pendingPlcStep is { } pending)
            {
                var ack = site.PlcValues.StepAck;
                if (pending == 0 || ack == pending)
                {
                    _pendingPlcStep = null;
                    Advance(site, network);
                }
                else if (ack > pending)
                {
                    _pendingPlcStep = null;
                }
            }
            return
            [
                .. network.Gates.Select(g => (AlarmDefinitions.GateDeviation(g), _gateCount.GetValueOrDefault(g) >= GateSteps)),
                .. network.Tanks.Select(t => (AlarmDefinitions.LevelFrozen(t), _frozenCount.GetValueOrDefault(t) >= FrozenSteps)),
            ];
        }
    }

    private void Advance(SiteSnapshot site, DrainageNetwork network)
    {
        foreach (var gate in network.Gates)
        {
            var setpoint = site.PlcValues.Setpoint(gate)?.Value;
            var opening = site.FieldValues.Opening(gate);
            var deviates = setpoint is { } s && opening is { } o && s.DiffersFrom(o, GateTolerance);
            _gateCount[gate] = deviates ? _gateCount.GetValueOrDefault(gate) + 1 : 0;
        }
        foreach (var tank in network.Tanks)
        {
            if (site.PlcValues.Level(tank)?.Value is not { } level)
            {
                continue;
            }
            var unchanged = _lastLevel.TryGetValue(tank, out var last) && level == last;
            var suspicious = network.IsBetweenEmptyAndFull(level, FrozenMargin);
            _frozenCount[tank] = unchanged && suspicious ? _frozenCount.GetValueOrDefault(tank) + 1 : 0;
            _lastLevel[tank] = level;
        }
    }
}
