using StormBench.Domain.Control;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Site;

/// <summary>Whether a node or device has announced itself and is alive.</summary>
public enum Presence
{
    /// <summary>No birth seen yet.</summary>
    Unknown,
    Online,

    /// <summary>Death certificate, lost broker or broken sequence: the last values are stale.</summary>
    Offline
}

/// <summary>A value with its quality: false once its source went offline, so the value is stale.</summary>
public readonly record struct Reading<T>(T Value, bool Good)
    where T : struct;

/// <summary>One of the PLC's stations in its own order 1..4: the gate and the tank it drains.</summary>
public sealed record PlcStation(GateId Gate, TankId Tank);

/// <summary>The last values of the PLC (device PLC01 behind edge gateway Edge01).</summary>
public interface IPlcReadings
{
    /// <summary>The tanks whose levels the PLC measures, in its own order.</summary>
    IReadOnlyList<TankId> Tanks { get; }

    /// <summary>The controlled stations in the PLC's order 1..4, the order of the EFD parameters.</summary>
    IReadOnlyList<PlcStation> Stations { get; }

    Reading<ControlMode>? CmdMode { get; }

    Reading<ControlMode>? ModeActive { get; }

    Reading<bool>? CenterOk { get; }

    PlcStatus? Status { get; }

    /// <summary>The lockstep step whose values the PLC has published.</summary>
    long? StepAck { get; }

    /// <summary>Tank level in metres.</summary>
    Reading<double>? Level(TankId tank);

    Reading<GateOpening>? Setpoint(GateId gate);

    /// <summary>The center's last proposal for a gate, as the PLC holds it.</summary>
    GateOpening? CenterSetpoint(GateId gate);
}

/// <summary>The last values of the simulated field and the test console (node Sim01).</summary>
public interface IFieldReadings
{
    ScenarioState State { get; }

    /// <summary>The simulator's run number, 0 before its first run.</summary>
    long? Run { get; }

    StormId? Storm { get; }

    AlgorithmId? Algorithm { get; }

    double? Pace { get; }

    /// <summary>The control step of the run the field reported last.</summary>
    long? CurrentStep { get; }

    long? Steps { get; }

    /// <summary>The PLC step this report belongs to, to pair it with the PLC's values.</summary>
    long? PlcStep { get; }

    /// <summary>Simulated time of the current run (model time, no time zone), or null before the first run.</summary>
    DateTime? Clock { get; }

    string? Message { get; }

    long? Mismatches { get; }

    bool? CenterLinkCut { get; }

    /// <summary>Mode 3: whether the center answered the last step that asked it.</summary>
    bool? CenterLinkUp { get; }

    GateId? StuckGate { get; }

    TankId? FrozenSensor { get; }

    /// <summary>Rain intensity in mm/h.</summary>
    double? Rain { get; }

    Volume? OverflowTotal { get; }

    Volume? BaseCaseOverflowTotal { get; }

    Volume? WwtpInflow { get; }

    /// <summary>Whether the point spilled during the last control step.</summary>
    bool? OverflowActive(OverflowPointId point);

    Volume? OverflowVolume(OverflowPointId point);

    /// <summary>Where the gate really is (position feedback), whatever the PLC commands.</summary>
    GateOpening? Opening(GateId gate);

    /// <summary>The tank's real level in metres, whatever its sensor reports.</summary>
    double? TrueLevel(TankId tank);
}

/// <summary>
/// The Astlingen site as the Primary Host sees it: the edge gateway, its PLC and the simulated
/// field node, with the network and the scenario catalog the simulator published.
/// </summary>
public sealed record SiteSnapshot(
    bool BrokerConnected,
    bool BrokerSeen,
    Presence Edge,
    Presence Plc,
    Presence Sim,
    IPlcReadings PlcValues,
    IFieldReadings FieldValues,
    DrainageNetwork? Network,
    ScenarioCatalog? Catalog)
{
    /// <summary>The broker as a participant: waiting until Scada reached it once, offline when it lost it.</summary>
    public Presence Broker => BrokerConnected ? Presence.Online : BrokerSeen ? Presence.Offline : Presence.Unknown;

    public DateTime? SimClock => FieldValues.Clock;
}

/// <summary>Reads the site as it is now.</summary>
public interface ISiteReader
{
    /// <summary>Incremented by every change, so displays can skip rendering when nothing happened.</summary>
    long Version { get; }

    SiteSnapshot Read();
}

/// <summary>Whether Scada holds its session with the MQTT broker right now.</summary>
public interface IBrokerConnection
{
    bool IsConnected { get; }
}
