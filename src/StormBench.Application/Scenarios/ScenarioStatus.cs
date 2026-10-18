using StormBench.Application.Site;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Scenarios;

/// <summary>What the simulator reports about the storm it runs (node Sim01).</summary>
public sealed record ScenarioStatus(
    Presence Simulator,
    ScenarioState State,
    long Run,
    StormId? Storm,
    AlgorithmId? Algorithm,
    double? Pace,
    long Step,
    long Steps,
    DateTime? Clock,
    string? Message,
    long Mismatches,
    ScenarioCatalog? Catalog,
    bool CenterLinkCut,
    bool? CenterLinkUp,
    GateId? StuckGate,
    TankId? FrozenSensor)
{
    public bool Busy => State.IsBusy;

    public bool CanStart => Simulator is Presence.Online && !Busy && Catalog is not null;

    public bool CanStop => Simulator is Presence.Online && Busy;

    public double Progress => Steps > 0 ? Math.Clamp((double)Step / Steps, 0.0, 1.0) : 0.0;

    public bool CanInjectFaults => Simulator is Presence.Online;

    public static ScenarioStatus From(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var field = site.FieldValues;
        return new ScenarioStatus(
            site.Sim,
            field.State,
            field.Run ?? 0,
            field.Storm,
            field.Algorithm,
            field.Pace,
            field.CurrentStep ?? 0,
            field.Steps ?? 0,
            site.SimClock,
            field.Message,
            field.Mismatches ?? 0,
            site.Catalog,
            field.CenterLinkCut ?? false,
            field.CenterLinkUp,
            field.StuckGate,
            field.FrozenSensor);
    }
}

/// <summary>Reads the test console's status from the site.</summary>
public sealed class GetScenarioStatusHandler(ISiteReader site)
{
    public ScenarioStatus Handle() => ScenarioStatus.From(site.Read());
}
