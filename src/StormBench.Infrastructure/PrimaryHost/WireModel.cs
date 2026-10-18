using StormBench.Contracts;
using StormBench.Domain.Control;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using ControlMode = StormBench.Domain.Control.ControlMode;
using ScenarioCatalog = StormBench.Domain.Scenarios.ScenarioCatalog;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>Values of the metric Scenario/State (<see cref="ScenarioStates"/>) as domain states.</summary>
public static class ScenarioStateWire
{
    /// <summary>The state the simulator reports; idle when it reports none (or one this Scada does not know).</summary>
    public static ScenarioState Parse(string? state) => state switch
    {
        ScenarioStates.Preparing => ScenarioState.Preparing,
        ScenarioStates.Running => ScenarioState.Running,
        ScenarioStates.Finished => ScenarioState.Finished,
        ScenarioStates.Stopped => ScenarioState.Stopped,
        ScenarioStates.Failed => ScenarioState.Failed,
        _ => ScenarioState.Idle,
    };
}

public static class NetworkDescriptionMapping
{
    extension(NetworkDescription description)
    {
        /// <summary>The network of the simulator's model as the domain sees it.</summary>
        public DrainageNetwork ToDomain()
        {
            var tanks = description.Tanks.Select(t => new TankId(t)).ToList();
            var tankSet = description.Tanks.ToHashSet(StringComparer.Ordinal);
            return new DrainageNetwork(
                description.Name,
                TimeSpan.FromSeconds(description.ControlStepS),
                description.TankMaxDepthM,
                description.Wwtp,
                tanks,
                [.. description.OverflowPoints.Select(p => new OverflowPoint(new OverflowPointId(p), tankSet.Contains(p) ? new TankId(p) : null))],
                [
                    .. description.Stations.Select(s => new Station(
                        new GateId(s.Valve), new TankId(s.Tank), new GateOpening(s.BaseSetting), new GateOpening(s.LowSetting))),
                ],
                [.. description.Nodes.Select(n => new NetworkNode(n.Id, NodeKindOf(n.Kind), n.X, n.Y))],
                [.. description.Links.Select(l => new NetworkLink(l.Id, LinkKindOf(l.Kind), l.From, l.To))]);
        }
    }

    private static NodeKind NodeKindOf(string kind) => kind switch
    {
        "junction" => NodeKind.Junction,
        "storage" => NodeKind.Storage,
        "outfall" => NodeKind.Outfall,
        _ => NodeKind.Other,
    };

    private static LinkKind LinkKindOf(string kind) => kind switch
    {
        "conduit" => LinkKind.Conduit,
        "orifice" => LinkKind.Orifice,
        "weir" => LinkKind.Weir,
        "pump" => LinkKind.Pump,
        "outlet" => LinkKind.Outlet,
        _ => LinkKind.Other,
    };
}

public static class ScenarioCatalogMapping
{
    extension(Contracts.ScenarioCatalog catalog)
    {
        /// <summary>The catalog of the test console as the domain sees it.</summary>
        public ScenarioCatalog ToDomain() => new(
            [.. catalog.Storms.Select(s => new StormWindow(new StormId(s.Id), s.Label, s.Description, s.Start, s.End, s.Steps))],
            [.. catalog.Algorithms.Select(a => new ControlAlgorithm(new AlgorithmId(a.Id), a.Label, a.Description, (ControlMode)a.Mode, CenterOf(a.Center)))],
            catalog.Paces,
            new StormId(catalog.DefaultStorm),
            new AlgorithmId(catalog.DefaultAlgorithm),
            catalog.DefaultPace);
    }

    /// <summary><see cref="Centers"/>: Scada itself, or any other center that proposes instead of it.</summary>
    private static ControlCenter? CenterOf(string? center) => center switch
    {
        null => null,
        Centers.Scada => ControlCenter.Scada,
        _ => ControlCenter.Simulator,
    };
}
