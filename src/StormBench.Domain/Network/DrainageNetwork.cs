namespace StormBench.Domain.Network;

public enum NodeKind
{
    Junction,
    Storage,
    Outfall,
    Other
}

public enum LinkKind
{
    Conduit,
    Orifice,
    Weir,
    Pump,
    Outlet,
    Other
}

/// <summary>A node of the sewer graph at its position in map units.</summary>
public sealed record NetworkNode(string Id, NodeKind Kind, double X, double Y);

/// <summary>A link of the sewer graph from one node to another.</summary>
public sealed record NetworkLink(string Id, LinkKind Kind, string From, string To);

/// <summary>
/// A controlled outlet: the gate of a tank, with the settings of the Base Case and the low
/// setting EFD throttles the emptiest tank to.
/// </summary>
public sealed record Station(GateId Gate, TankId Tank, GateOpening BaseSetting, GateOpening LowSetting);

/// <summary>A place that spills into the river: a tank, or a weir without storage when <see cref="Tank"/> is null.</summary>
public sealed record OverflowPoint(OverflowPointId Id, TankId? Tank)
{
    public bool IsTank => Tank is not null;
}

/// <summary>
/// The drainage network as the simulator loaded it: tanks of one depth, the controlled stations,
/// the overflow points and the sewer graph between them.
/// </summary>
public sealed class DrainageNetwork(
    string name,
    TimeSpan controlStep,
    double tankDepth,
    string wwtp,
    IReadOnlyList<TankId> tanks,
    IReadOnlyList<OverflowPoint> overflowPoints,
    IReadOnlyList<Station> stations,
    IReadOnlyList<NetworkNode> nodes,
    IReadOnlyList<NetworkLink> links)
{
    public string Name { get; } = name;

    public TimeSpan ControlStep { get; } = controlStep;

    /// <summary>Depth of every tank in metres: a level at this depth is a full tank.</summary>
    public double TankDepth { get; } = tankDepth > 0 ? tankDepth : throw new ArgumentOutOfRangeException(nameof(tankDepth));

    /// <summary>The outfall to the treatment plant.</summary>
    public string Wwtp { get; } = wwtp;

    public IReadOnlyList<TankId> Tanks { get; } = tanks;

    public IReadOnlyList<OverflowPoint> OverflowPoints { get; } = overflowPoints;

    public IReadOnlyList<Station> Stations { get; } = stations;

    public IReadOnlyList<NetworkNode> Nodes { get; } = nodes;

    public IReadOnlyList<NetworkLink> Links { get; } = links;

    /// <summary>Overflow structures without storage.</summary>
    public IEnumerable<OverflowPoint> Weirs => OverflowPoints.Where(p => !p.IsTank);

    public IEnumerable<GateId> Gates => Stations.Select(s => s.Gate);

    public bool HasTank(TankId tank) => Tanks.Contains(tank);

    public bool HasGate(GateId gate) => Stations.Any(s => s.Gate == gate);

    public Station? StationOf(GateId gate) => Stations.FirstOrDefault(s => s.Gate == gate);

    public Station? StationAt(TankId tank) => Stations.FirstOrDefault(s => s.Tank == tank);

    public FillFraction Fill(double level) => FillFraction.Of(level, TankDepth);

    /// <summary>Whether a level is more than a margin away from both the empty and the full tank.</summary>
    public bool IsBetweenEmptyAndFull(double level, double margin) => level > margin && level < TankDepth - margin;
}
