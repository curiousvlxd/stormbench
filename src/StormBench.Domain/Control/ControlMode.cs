namespace StormBench.Domain.Control;

/// <summary>Control modes of the PLC, identical to plc/src/main.st and simulator/stormbench_sim/control.py.</summary>
public enum ControlMode : long
{
    Local = 0,
    EfdPublished = 1,
    EfdIntended = 2,
    EfdCenter = 3
}

/// <summary>Who proposes the setpoints in mode 3: Scada's own optimiser, or the simulator playing the center (ADR 0017).</summary>
public enum ControlCenter
{
    Scada,
    Simulator
}

/// <summary>Status bits the PLC reports with its values.</summary>
[Flags]
public enum PlcStatus : long
{
    None = 0,

    /// <summary>The stations fell back to local control.</summary>
    Fallback = 1,

    /// <summary>A level measurement is implausible: the stations hold their local settings.</summary>
    InputFault = 2,

    /// <summary>A setpoint was limited to the station's safe range.</summary>
    Clamped = 4,

    /// <summary>The center's proposal was rejected as implausible.</summary>
    BadProposal = 8
}
