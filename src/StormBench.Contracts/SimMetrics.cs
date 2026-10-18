namespace StormBench.Contracts;

/// <summary>
/// Metric names of edge node Sim01: the simulated field (rain gauges, overflow structures, the
/// inflow to the treatment plant) and the test console of the testbed. All metrics are node
/// metrics (NBIRTH, NDATA); commands arrive as NCMD. Python mirror:
/// simulator/stormbench_sim/uns_contract.py, kept in step by a test.
/// </summary>
public static class SimMetrics
{
    public const string Network = "Model/Network";
    public const string Catalog = "Scenario/Catalog";

    public const string State = "Scenario/State";
    public const string Run = "Scenario/Run";
    public const string Storm = "Scenario/Storm";
    public const string Algorithm = "Scenario/Algorithm";
    public const string Pace = "Scenario/Pace";
    public const string Step = "Scenario/Step";
    public const string Steps = "Scenario/Steps";
    public const string Clock = "Scenario/Clock";
    public const string Message = "Scenario/Message";

    public const string Start = "Scenario/Start";
    public const string Stop = "Scenario/Stop";

    public const string RainIntensity = "Rain/Intensity";
    public const string OverflowTotal = "Overflow/Total";
    public const string WwtpInflow = "WWTP/Inflow";
    public const string BaseCaseOverflowTotal = "Reference/BaseCase/OverflowTotal";
    public const string Mismatches = "Check/Mismatches";

    public const string CenterLinkCut = "Fault/CenterLinkCut";

    /// <summary>Mode 3: whether the center answered the last step that asked it (false while cut or lost; the PLC then runs locally).</summary>
    public const string CenterLinkUp = "Center/LinkUp";

    /// <summary>String: the controlled outlet that keeps its opening, or empty.</summary>
    public const string StuckGate = "Fault/StuckGate";

    /// <summary>String: the tank whose level reading freezes, or empty.</summary>
    public const string FrozenSensor = "Fault/FrozenSensor";

    /// <summary>Int64: the PLC StepReq of the step this report belongs to, to pair it with the PLC's values.</summary>
    public const string PlcStep = "Lockstep/PlcStep";

    /// <summary>Double: opening of a controlled outlet as the model applies it (position feedback).</summary>
    public static string ValveOpening(string valve) => $"Outlets/{valve}/Opening";

    /// <summary>Double: the tank's real level in the model, whatever its sensor reports.</summary>
    public static string TrueLevel(string tank) => $"Storage/{tank}/TrueLevel";

    /// <summary>Boolean: more than 0.1 m³ spilled at the point during the last control step.</summary>
    public static string OverflowActive(string point) => $"Overflow/{point}/Active";

    /// <summary>Double: m³ spilled at the point since the run started.</summary>
    public static string OverflowVolume(string point) => $"Overflow/{point}/Volume";
}

/// <summary>Values of <see cref="SimMetrics.State"/>.</summary>
public static class ScenarioStates
{
    public const string Idle = "idle";
    public const string Preparing = "preparing";
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Stopped = "stopped";
    public const string Failed = "failed";

    public static bool IsBusy(string? state) => state is Preparing or Running;
}
