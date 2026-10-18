using StormBench.Domain.Network;

namespace StormBench.Domain.Alarms;

/// <summary>The alarms of the Astlingen site: communication, PLC status, overflows and the plausibility checks.</summary>
public static class AlarmDefinitions
{
    public static readonly AlarmDefinition Broker =
        new("Scada01.Broker", "Scada01", "No connection to the MQTT broker, all values stale", AlarmPriority.High);

    public static readonly AlarmDefinition EdgeOffline =
        new("Edge01.Offline", "Edge01", "Edge gateway offline, PLC values stale", AlarmPriority.High);

    public static readonly AlarmDefinition PlcOffline =
        new("PLC01.Offline", "PLC01", "PLC not reachable, values stale", AlarmPriority.High);

    public static readonly AlarmDefinition SimOffline =
        new("Sim01.Offline", "Sim01", "Simulator offline, field values stale", AlarmPriority.Low);

    public static readonly AlarmDefinition InputFault =
        new("PLC01.InputFault", "PLC01", "Implausible level measurement, stations hold local settings", AlarmPriority.High);

    public static readonly AlarmDefinition Fallback =
        new("PLC01.Fallback", "PLC01", "Stations fell back to local control", AlarmPriority.Medium);

    public static readonly AlarmDefinition BadProposal =
        new("PLC01.BadProposal", "PLC01", "Center proposal rejected as implausible", AlarmPriority.Medium);

    public static readonly AlarmDefinition Clamped =
        new("PLC01.Clamped", "PLC01", "Setpoint limited to the station's safe range", AlarmPriority.Low);

    public static AlarmDefinition Overflow(OverflowPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return Overflow(point.Id, point.IsTank);
    }

    public static AlarmDefinition Overflow(OverflowPointId point, bool isTank) =>
        new(OverflowId(point), point.Value, isTank ? $"Tank {point} overflowing" : $"{point} overflowing", AlarmPriority.High);

    /// <summary>A controlled outlet's position feedback differs from the PLC setpoint.</summary>
    public static AlarmDefinition GateDeviation(GateId gate) =>
        new(GateDeviationId(gate), gate.Value, $"Gate {gate} does not follow its setpoint", AlarmPriority.Medium);

    /// <summary>A tank level reading stopped moving although the tank is neither empty nor full.</summary>
    public static AlarmDefinition LevelFrozen(TankId tank) =>
        new(LevelFrozenId(tank), tank.Value, $"Level reading of {tank} frozen", AlarmPriority.Medium);

    public static string OverflowId(OverflowPointId point) => $"{point}.Overflow";

    public static string GateDeviationId(GateId gate) => $"{gate}.GateDeviation";

    public static string LevelFrozenId(TankId tank) => $"{tank}.LevelFrozen";
}
