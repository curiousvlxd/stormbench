using StormBench.Sparkplug.Topics;

namespace StormBench.Contracts;

/// <summary>
/// Unified namespace of the testbed, carried by Sparkplug B 3.0 topics:
/// spBv1.0/{group}/{message type}/{edge node}[/{device}].
/// ISA-95 reading: the group is the site (the Astlingen drainage network), the edge node is
/// the gateway next to the PLC, the device is the PLC that controls the basins. Metric names
/// continue the hierarchy below the device (Storage/T2/Level, Outlets/V2/Setpoint).
/// Edge node Sim01 is the simulator: it stands in for the field instruments a real network
/// reports through its own telemetry (rain gauges, overflow measurements) and runs the test
/// console (<see cref="SimMetrics"/>).
/// </summary>
public static class Uns
{
    public const string GroupId = "Astlingen";
    public const string EdgeNodeId = "Edge01";
    public const string PlcDeviceId = "PLC01";
    public const string SimNodeId = "Sim01";
    public const string PrimaryHostId = "Scada01";

    public static string NodeTopic(string messageType) => new SparkplugTopic(GroupId, messageType, EdgeNodeId, null).ToString();

    public static string DeviceTopic(string messageType, string deviceId = PlcDeviceId) =>
        new SparkplugTopic(GroupId, messageType, EdgeNodeId, deviceId).ToString();

    public static string StateTopic() => SparkplugTopic.State(PrimaryHostId);

    public static string GroupFilter() => SparkplugTopic.GroupFilter(GroupId);
}
