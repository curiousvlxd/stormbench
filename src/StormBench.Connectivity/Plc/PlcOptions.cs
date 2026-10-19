namespace StormBench.Connectivity.Plc;

/// <summary>How the gateway reaches the PLC, from the section <c>Plc</c>.</summary>
public sealed class PlcOptions
{
    public const string Section = "Plc";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 502;

    /// <summary>OpenPLC v4 answers unit id 1 only.</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>Pause between two reads of the register image; zero polls back to back.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.Zero;

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How the PLC's register image is read; proposals are always written over Modbus.</summary>
    public PlcDriver Driver { get; set; } = PlcDriver.Modbus;

    public string OpcUaEndpoint { get; set; } = "opc.tcp://127.0.0.1:4840/openplc/opcua";
}
