using Microsoft.Extensions.Time.Testing;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Scada.Tests;

/// <summary>A UNS state fed with hand-made Sparkplug messages of Edge01/PLC01 and Sim01.</summary>
internal sealed class SiteFixture : IBrokerConnection
{
    public const string NetworkJson = """
        {"name": "mini", "controlStepS": 300, "tankMaxDepthM": 5.0, "wwtp": "Out",
         "tanks": ["T1", "T2"], "overflowPoints": ["T1", "T2", "CSO1"],
         "stations": [{"valve": "V2", "tank": "T2", "baseSetting": 0.3, "lowSetting": 0.1}],
         "nodes": [
           {"id": "T1", "kind": "storage", "x": 5000, "y": 3000},
           {"id": "T2", "kind": "storage", "x": 2000, "y": 3000},
           {"id": "CSO1", "kind": "junction", "x": 5200, "y": 3300},
           {"id": "J1", "kind": "junction", "x": 3500, "y": 3000},
           {"id": "Out", "kind": "outfall", "x": 7000, "y": 3000}],
         "links": [
           {"id": "V2", "kind": "orifice", "from": "T2", "to": "J1"},
           {"id": "C1", "kind": "conduit", "from": "J1", "to": "T1"},
           {"id": "C2", "kind": "conduit", "from": "CSO1", "to": "J1"},
           {"id": "V1", "kind": "orifice", "from": "T1", "to": "Out"}],
         "labelOffsets": {"CSO1": [0, 700]}}
        """;

    public const string CatalogJson = """
        {"storms": [
           {"id": "s1", "label": "Storm one", "description": "Short", "start": "2000-05-30T00:00:00", "end": "2000-05-31T12:00:00", "steps": 431},
           {"id": "s2", "label": "Storm two", "description": "Long", "start": "2000-10-04T12:00:00", "end": "2000-10-05T12:00:00", "steps": 287}],
         "algorithms": [
           {"id": "base", "label": "Base Case", "description": "Fixed settings", "mode": 0},
           {"id": "efd-center", "label": "EFD from the center", "description": "Center proposes", "mode": 3}],
         "paces": [0.2, 0.1, 0.0], "defaultStorm": "s1", "defaultAlgorithm": "efd-center", "defaultPace": 0.1}
        """;

    private ulong _edgeSeq;
    private ulong _simSeq;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

    public UnsState State { get; }

    public bool BrokerConnected { get; set; } = true;

    bool IBrokerConnection.IsConnected => BrokerConnected;

    public UnsSiteReader Reader { get; }

    public SiteFixture()
    {
        State = new UnsState(Clock);
        Reader = new UnsSiteReader(State, this);
    }

    public SiteSnapshot Read() => Reader.Read();

    public void PlcBirth(params SparkplugMetric[] metrics)
    {
        _edgeSeq = 0;
        State.Apply(Topic(Uns.EdgeNodeId, MessageTypes.NBirth), new SparkplugPayload(0, 0, [SparkplugMetric.Int64(PlcMetrics.BdSeq, 1)]));
        State.Apply(Topic(Uns.EdgeNodeId, MessageTypes.DBirth, Uns.PlcDeviceId), new SparkplugPayload(0, ++_edgeSeq, metrics));
    }

    public void PlcData(params SparkplugMetric[] metrics) =>
        State.Apply(Topic(Uns.EdgeNodeId, MessageTypes.DData, Uns.PlcDeviceId), new SparkplugPayload(0, ++_edgeSeq % 256, metrics));

    public void EdgeDeath() =>
        State.Apply(Topic(Uns.EdgeNodeId, MessageTypes.NDeath), new SparkplugPayload(0, null, [SparkplugMetric.Int64(PlcMetrics.BdSeq, 1)]));

    public void PlcDeath() =>
        State.Apply(Topic(Uns.EdgeNodeId, MessageTypes.DDeath, Uns.PlcDeviceId), new SparkplugPayload(0, ++_edgeSeq % 256, []));

    /// <summary>Sim01 birth with the network, the catalog, an idle console and the given metrics.</summary>
    public void SimBirth(params SparkplugMetric[] metrics)
    {
        _simSeq = 0;
        SparkplugMetric[] basics =
        [
            SparkplugMetric.Int64(PlcMetrics.BdSeq, 1),
            SparkplugMetric.String(SimMetrics.Network, NetworkJson),
            SparkplugMetric.String(SimMetrics.Catalog, CatalogJson),
            SparkplugMetric.String(SimMetrics.State, ScenarioStates.Idle),
            SparkplugMetric.Int64(SimMetrics.Run, 0)
        ];
        State.Apply(Topic(Uns.SimNodeId, MessageTypes.NBirth), new SparkplugPayload(0, 0, [.. basics, .. metrics]));
    }

    public void SimData(params SparkplugMetric[] metrics) =>
        State.Apply(Topic(Uns.SimNodeId, MessageTypes.NData), new SparkplugPayload(0, ++_simSeq % 256, metrics));

    public void SimDeath() =>
        State.Apply(Topic(Uns.SimNodeId, MessageTypes.NDeath), new SparkplugPayload(0, null, [SparkplugMetric.Int64(PlcMetrics.BdSeq, 1)]));

    /// <summary>Unix milliseconds of a model time, as Sim01 publishes Scenario/Clock.</summary>
    public static long ClockMs(DateTime modelTime) =>
        (long)(DateTime.SpecifyKind(modelTime, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalMilliseconds;

    private static SparkplugTopic Topic(string node, string type, string? device = null) => new(Uns.GroupId, type, node, device);
}
