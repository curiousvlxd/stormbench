using StormBench.Application.Site;
using StormBench.Domain.Alarms;
using StormBench.Domain.Scenarios;

namespace StormBench.Scada.Hmi.Live;

/// <summary>Fill and TrueFill as fractions of the tank depth; TrueFill only while the reading is wrong (frozen sensor).</summary>
public sealed record LiveTank(string Id, double? Fill, double? TrueFill, bool Overflowing, string? Alarm);

/// <summary>Setpoint: what the PLC commands; Opening: where the gate really is (position feedback).</summary>
public sealed record LiveGate(string Id, double? Setpoint, double? Opening, string? Alarm);

public sealed record LiveWeir(string Id, bool Overflowing, string? Alarm);

/// <summary>
/// What the 3D view needs at one moment, sent to the browser on every change.
/// Link: "unused" (the PLC does not ask the center), "up", "cut" (by the console) or "lost".
/// Plc: "online", "offline" or "waiting"; PlcLocal: the stations run on their own although the
/// center is asked (mode 3 fallback). Hour: the time of day of the simulated clock, in hours, so the
/// town can have its day and night.
/// </summary>
public sealed record LiveFrame(
    bool Running,
    double? Rain,
    IReadOnlyList<LiveTank> Tanks,
    IReadOnlyList<LiveGate> Gates,
    IReadOnlyList<LiveWeir> Weirs,
    IReadOnlyDictionary<string, double> Volumes,
    double? Wwtp,
    string Link,
    string Plc,
    bool PlcLocal,
    double? Hour)
{
    public static LiveFrame From(HmiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var site = view.Site;
        var diagram = view.Diagram;
        var tanks = diagram?.Tanks.Select(t => new LiveTank(
            t.Shape.Id, t.Fill, t.TrueFill, t.Overflowing, Priority(t.Alarm))).ToList() ?? [];
        var gates = diagram?.Valves.Where(v => v.Shape.Controlled).Select(v => new LiveGate(
            v.Shape.Id, v.Setpoint, v.Opening, Priority(v.Alarm))).ToList() ?? [];
        var weirs = diagram?.Weirs.Select(w => new LiveWeir(w.Shape.Id, w.Overflowing, Priority(w.Alarm))).ToList() ?? [];
        var volumes = (site.Network?.OverflowPoints ?? [])
            .Select(p => (p.Id, Volume: site.FieldValues.OverflowVolume(p.Id)))
            .Where(x => x.Volume is not null)
            .ToDictionary(x => x.Id.Value, x => x.Volume!.Value.CubicMeters);
        return new LiveFrame(
            view.Scenario.State is ScenarioState.Running,
            view.Rain,
            tanks,
            gates,
            weirs,
            volumes,
            view.Kpi.WwtpInflow?.CubicMeters,
            LinkState(view),
            site.Plc switch { Presence.Online => "online", Presence.Offline => "offline", _ => "waiting" },
            LocalFallback(view),
            view.Scenario.Clock?.TimeOfDay.TotalHours);
    }

    /// <summary>The PLC is asked to take the center's proposals (mode 3) and runs its stations locally instead.</summary>
    public static bool LocalFallback(HmiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Site.Plc is Presence.Online && view.CenterAsked && view.PlcFallback;
    }

    public static string LinkState(HmiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Scenario.CenterLinkCut)
        {
            return "cut";
        }
        if (!view.CenterAsked)
        {
            return "unused";
        }
        return view.Scenario.CenterLinkUp == false ? "lost" : "up";
    }

    private static string? Priority(Alarm? alarm) => alarm is { Active: true } a
        ? a.Definition.Priority switch
        {
            AlarmPriority.High => "high",
            AlarmPriority.Medium => "medium",
            _ => "low"
        }
        : null;
}
