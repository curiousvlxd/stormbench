using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Domain.Control;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using ControlMode = StormBench.Domain.Control.ControlMode;
using ScenarioCatalog = StormBench.Domain.Scenarios.ScenarioCatalog;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>Typed access to the metrics of one node or device.</summary>
public sealed class MetricBag(IEnumerable<MetricSnapshot> metrics)
{
    private readonly Dictionary<string, MetricSnapshot> _metrics = metrics.ToDictionary(m => m.Name);

    public static MetricBag Empty { get; } = new([]);

    public MetricSnapshot? Get(string name) => _metrics.GetValueOrDefault(name);

    public double? GetDouble(string name) => Get(name)?.Value is double d ? d : null;

    public long? GetLong(string name) => Get(name)?.Value is long l ? l : null;

    public bool? GetBool(string name) => Get(name)?.Value is bool b ? b : null;

    public string? GetString(string name) => Get(name)?.Value as string;

    /// <summary>A string metric, null when it is missing or empty.</summary>
    public string? GetText(string name) => GetString(name) is { Length: > 0 } text ? text : null;

    public Reading<double>? ReadDouble(string name) => Get(name) is { Value: double d } m ? new(d, m.Quality is Quality.Good) : null;

    public Reading<long>? ReadLong(string name) => Get(name) is { Value: long l } m ? new(l, m.Quality is Quality.Good) : null;

    public Reading<bool>? ReadBool(string name) => Get(name) is { Value: bool b } m ? new(b, m.Quality is Quality.Good) : null;
}

/// <summary>The PLC's metrics of device PLC01 (<see cref="PlcMetrics"/>).</summary>
public sealed class UnsPlcReadings(MetricBag metrics) : IPlcReadings
{
    private static readonly TankId[] PlcTanks = [.. PlcMetrics.Tanks.Select(t => new TankId(t))];

    private static readonly PlcStation[] PlcStations =
        [.. PlcMetrics.ControlledValves.Zip(PlcMetrics.ControlledTanks, (valve, tank) => new PlcStation(new GateId(valve), new TankId(tank)))];

    public IReadOnlyList<TankId> Tanks => PlcTanks;

    public IReadOnlyList<PlcStation> Stations => PlcStations;

    public Reading<ControlMode>? CmdMode => Mode(PlcMetrics.CmdMode);

    public Reading<ControlMode>? ModeActive => Mode(PlcMetrics.ModeActive);

    public Reading<bool>? CenterOk => metrics.ReadBool(PlcMetrics.CenterOk);

    public PlcStatus? Status => metrics.GetLong(PlcMetrics.Status) is { } status ? (PlcStatus)status : null;

    public long? StepAck => metrics.GetLong(PlcMetrics.StepAck);

    public Reading<double>? Level(TankId tank) => metrics.ReadDouble(PlcMetrics.Level(tank.Value));

    public Reading<GateOpening>? Setpoint(GateId gate) =>
        metrics.ReadDouble(PlcMetrics.Setpoint(gate.Value)) is { } r ? new(new GateOpening(r.Value), r.Good) : null;

    public GateOpening? CenterSetpoint(GateId gate) =>
        metrics.GetDouble(PlcMetrics.CenterSetpoint(gate.Value)) is { } setpoint ? new GateOpening(setpoint) : null;

    private Reading<ControlMode>? Mode(string name) =>
        metrics.ReadLong(name) is { } r ? new((ControlMode)r.Value, r.Good) : null;
}

/// <summary>The simulated field and the test console of node Sim01 (<see cref="SimMetrics"/>).</summary>
public sealed class UnsFieldReadings(MetricBag metrics) : IFieldReadings
{
    public ScenarioState State => ScenarioStateWire.Parse(metrics.GetString(SimMetrics.State));

    public long? Run => metrics.GetLong(SimMetrics.Run);

    public StormId? Storm => metrics.GetText(SimMetrics.Storm) is { } storm ? new StormId(storm) : null;

    public AlgorithmId? Algorithm => metrics.GetText(SimMetrics.Algorithm) is { } algorithm ? new AlgorithmId(algorithm) : null;

    public double? Pace => metrics.GetDouble(SimMetrics.Pace);

    public long? CurrentStep => metrics.GetLong(SimMetrics.Step);

    public long? Steps => metrics.GetLong(SimMetrics.Steps);

    public long? PlcStep => metrics.GetLong(SimMetrics.PlcStep);

    public DateTime? Clock => metrics.GetLong(SimMetrics.Clock) is { } ms && ms > 0
        ? DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(ms), DateTimeKind.Unspecified)
        : null;

    public string? Message => metrics.GetText(SimMetrics.Message);

    public long? Mismatches => metrics.GetLong(SimMetrics.Mismatches);

    public bool? CenterLinkCut => metrics.GetBool(SimMetrics.CenterLinkCut);

    public bool? CenterLinkUp => metrics.GetBool(SimMetrics.CenterLinkUp);

    public GateId? StuckGate => metrics.GetText(SimMetrics.StuckGate) is { } gate ? new GateId(gate) : null;

    public TankId? FrozenSensor => metrics.GetText(SimMetrics.FrozenSensor) is { } tank ? new TankId(tank) : null;

    public double? Rain => metrics.GetDouble(SimMetrics.RainIntensity);

    public Volume? OverflowTotal => Cubic(SimMetrics.OverflowTotal);

    public Volume? BaseCaseOverflowTotal => Cubic(SimMetrics.BaseCaseOverflowTotal);

    public Volume? WwtpInflow => Cubic(SimMetrics.WwtpInflow);

    public bool? OverflowActive(OverflowPointId point) => metrics.GetBool(SimMetrics.OverflowActive(point.Value));

    public Volume? OverflowVolume(OverflowPointId point) => Cubic(SimMetrics.OverflowVolume(point.Value));

    public GateOpening? Opening(GateId gate) =>
        metrics.GetDouble(SimMetrics.ValveOpening(gate.Value)) is { } opening ? new GateOpening(opening) : null;

    public double? TrueLevel(TankId tank) => metrics.GetDouble(SimMetrics.TrueLevel(tank.Value));

    private Volume? Cubic(string name) => metrics.GetDouble(name) is { } m3 ? new Volume(m3) : null;
}

/// <summary>
/// Reads the site from the UNS state. The JSON descriptions of the network and the catalog are
/// parsed once per birth; <see cref="Description"/> keeps the network as published, for the
/// displays that draw its geometry and the town around it.
/// </summary>
public sealed class UnsSiteReader(UnsState uns, IBrokerConnection broker) : ISiteReader
{
    private readonly Lock _gate = new();
    private string? _networkJson;
    private NetworkDescription? _description;
    private DrainageNetwork? _network;
    private string? _catalogJson;
    private ScenarioCatalog? _catalog;
    private bool _brokerSeen;

    public long Version => uns.Version;

    /// <summary>The network description of the last <see cref="Read"/>, as the simulator published it.</summary>
    public NetworkDescription? Description
    {
        get
        {
            lock (_gate)
            {
                return _description;
            }
        }
    }

    public SiteSnapshot Read()
    {
        var connected = broker.IsConnected;
        var edge = Presence.Unknown;
        var plc = Presence.Unknown;
        var plcMetrics = MetricBag.Empty;
        if (uns.TryGetNode(Uns.GroupId, Uns.EdgeNodeId, out var edgeNode))
        {
            edge = edgeNode.Online ? Presence.Online : Presence.Offline;
            if (edgeNode.Devices.FirstOrDefault(d => d.DeviceId is Uns.PlcDeviceId) is { } device)
            {
                plc = device.Online && edgeNode.Online ? Presence.Online : Presence.Offline;
                plcMetrics = new MetricBag(device.Metrics);
            }
        }

        var sim = Presence.Unknown;
        var simMetrics = MetricBag.Empty;
        if (uns.TryGetNode(Uns.GroupId, Uns.SimNodeId, out var simNode))
        {
            sim = simNode.Online ? Presence.Online : Presence.Offline;
            simMetrics = new MetricBag(simNode.Metrics);
        }

        DrainageNetwork? network;
        ScenarioCatalog? catalog;
        lock (_gate)
        {
            var networkJson = simMetrics.GetString(SimMetrics.Network);
            if (!string.Equals(networkJson, _networkJson, StringComparison.Ordinal))
            {
                _networkJson = networkJson;
                _description = NetworkDescription.Parse(networkJson);
                _network = _description?.ToDomain();
            }
            var catalogJson = simMetrics.GetString(SimMetrics.Catalog);
            if (!string.Equals(catalogJson, _catalogJson, StringComparison.Ordinal))
            {
                _catalogJson = catalogJson;
                _catalog = Contracts.ScenarioCatalog.Parse(catalogJson)?.ToDomain();
            }
            network = _network;
            catalog = _catalog;
            _brokerSeen |= connected;
        }
        return new SiteSnapshot(
            connected, _brokerSeen || connected, edge, plc, sim, new UnsPlcReadings(plcMetrics), new UnsFieldReadings(simMetrics), network, catalog);
    }
}
