namespace StormBench.Connectivity.Plc;

/// <summary>How the gateway reads the PLC's register image.</summary>
public enum PlcDriver
{
    /// <summary>One FC3 read per snapshot, the lockstep path (ADR 0003).</summary>
    Modbus,

    /// <summary>Read over OPC UA (ADR 0009).</summary>
    OpcUa,
}
