namespace StormBench.Contracts;

/// <summary>Metric names of the edge node and the PLC device. One definition for publisher and consumers.</summary>
public static class PlcMetrics
{
    public const string BdSeq = "bdSeq";
    public const string Rebirth = "Node Control/Rebirth";

    public const string StepReq = "Lockstep/StepReq";
    public const string StepAck = "Lockstep/StepAck";
    public const string SimTime = "Lockstep/SimTime";
    public const string CmdMode = "Control/CmdMode";
    public const string CenterOk = "Control/CenterOk";
    public const string ModeActive = "Control/ModeActive";
    public const string Status = "Control/Status";
    public const string ScanCount = "Diagnostics/ScanCount";
    public const string CenterStepEcho = "Coordination/StepReceived";

    public const string CenterStep = "Coordination/Step";

    public static readonly IReadOnlyList<string> Tanks = ["T1", "T2", "T3", "T4", "T5", "T6"];
    public static readonly IReadOnlyList<string> ControlledValves = ["V2", "V3", "V4", "V6"];

    /// <summary>Tank of each controlled outlet, in PLC station order 1..4.</summary>
    public static readonly IReadOnlyList<string> ControlledTanks = ["T2", "T3", "T4", "T6"];

    public static string Level(string tank) => $"Storage/{tank}/Level";

    public static string Setpoint(string valve) => $"Outlets/{valve}/Setpoint";

    public static string CenterSetpoint(string valve) => $"Outlets/{valve}/CenterSetpoint";
}

/// <summary>CmdMode values, identical to plc/src/main.st and simulator/stormbench_sim/control.py.</summary>
public enum ControlMode : long
{
    Local = 0,
    EfdPublished = 1,
    EfdIntended = 2,
    EfdCenter = 3
}
