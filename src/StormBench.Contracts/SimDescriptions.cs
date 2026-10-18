using System.Text.Json;

namespace StormBench.Contracts;

/// <summary>
/// <see cref="SimMetrics.Network"/>: the drainage network as the simulator loaded it, so displays
/// draw the network from data instead of a hand-made picture. Coordinates are the model's map units.
/// </summary>
public sealed record NetworkDescription(
    string Name,
    int ControlStepS,
    double TankMaxDepthM,
    string Wwtp,
    IReadOnlyList<string> Tanks,
    IReadOnlyList<string> OverflowPoints,
    IReadOnlyList<StationDescription> Stations,
    IReadOnlyList<NodeDescription> Nodes,
    IReadOnlyList<LinkDescription> Links,
    IReadOnlyDictionary<string, double[]>? LabelOffsets,
    MapDescription? Map = null)
{
    public static NetworkDescription? Parse(string? json)
    {
        var network = SimJson.Parse<NetworkDescription>(json);
        return network is { Tanks: not null, OverflowPoints: not null, Stations: not null, Nodes: not null, Links: not null }
               && network.TankMaxDepthM > 0
            ? network
            : null;
    }
}

/// <summary>
/// The town around the sewers, in map units: subcatchments and rain gauges from the model, and the
/// receiving waters, areas and place names traced from the benchmark's own backdrop.
/// </summary>
public sealed record MapDescription(
    IReadOnlyList<CatchmentDescription>? Catchments,
    IReadOnlyList<GaugeDescription>? Gauges,
    IReadOnlyList<WaterDescription>? Waters,
    IReadOnlyList<AreaDescription>? Areas,
    IReadOnlyList<PlaceDescription>? Places,
    IReadOnlyDictionary<string, string>? Tints = null);

public sealed record CatchmentDescription(string Id, string RainGauge, string Outlet, double AreaHa, IReadOnlyList<double[]> Polygon);

public sealed record GaugeDescription(string Id, double X, double Y);

public sealed record WaterDescription(string Name, IReadOnlyList<double[]> Line);

public sealed record AreaDescription(string Name, IReadOnlyList<double[]> Polygon);

public sealed record PlaceDescription(string Name, double X, double Y);

public sealed record StationDescription(string Valve, string Tank, double BaseSetting, double LowSetting);

/// <summary>Kind: junction, storage or outfall.</summary>
public sealed record NodeDescription(string Id, string Kind, double X, double Y);

/// <summary>Kind: conduit, orifice, weir, pump or outlet.</summary>
public sealed record LinkDescription(string Id, string Kind, string From, string To);

/// <summary><see cref="SimMetrics.Catalog"/>: the storms and control algorithms the test console offers.</summary>
public sealed record ScenarioCatalog(
    IReadOnlyList<StormOption> Storms,
    IReadOnlyList<AlgorithmOption> Algorithms,
    IReadOnlyList<double> Paces,
    string DefaultStorm,
    string DefaultAlgorithm,
    double DefaultPace)
{
    public static ScenarioCatalog? Parse(string? json)
    {
        var catalog = SimJson.Parse<ScenarioCatalog>(json);
        return catalog is { Storms.Count: > 0, Algorithms.Count: > 0, Paces: not null } ? catalog : null;
    }

    public StormOption? Storm(string? id) => Storms.FirstOrDefault(s => s.Id == id);

    public AlgorithmOption? Algorithm(string? id) => Algorithms.FirstOrDefault(a => a.Id == id);
}

/// <summary>A storm window in model time (the model has no time zone).</summary>
public sealed record StormOption(string Id, string Label, string Description, DateTime Start, DateTime End, long Steps);

/// <summary>
/// Mode is the PLC's CmdMode for this algorithm (<see cref="ControlMode"/>). Center says who
/// proposes the setpoints in mode 3: <see cref="Centers.Scada"/> (the Optimization module over the
/// UNS) or <see cref="Centers.Simulator"/> (the simulator plays the center, ADR 0017); null for
/// algorithms that run in the PLC alone.
/// </summary>
public sealed record AlgorithmOption(string Id, string Label, string Description, long Mode, string? Center = null);

public static class Centers
{
    public const string Scada = "scada";
    public const string Simulator = "simulator";
}

internal static class SimJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T? Parse<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
