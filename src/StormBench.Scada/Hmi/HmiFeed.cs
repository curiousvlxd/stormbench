using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Kpi;
using StormBench.Application.Scenarios;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Domain.Alarms;
using StormBench.Domain.Control;
using StormBench.Domain.Kpi;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Scada.Hmi.Mnemonic;
using ControlMode = StormBench.Domain.Control.ControlMode;

namespace StormBench.Scada.Hmi;

/// <summary>TrueFill: the real level when a fault makes the reading wrong (frozen sensor), else null.</summary>
public sealed record TankView(TankShape Shape, double? Level, double? Fill, bool Stale, bool Overflowing, Alarm? Alarm, double? TrueFill = null);

/// <summary>Opening: position feedback from the field; Alarm: the gate does not follow.</summary>
public sealed record ValveView(ValveShape Shape, double? Setpoint, double? Proposal, bool Stale, double? Opening = null, Alarm? Alarm = null);

public sealed record WeirView(WeirShape Shape, bool Overflowing, double? Volume, bool Stale, Alarm? Alarm);

public sealed record DiagramView(
    DiagramLayout Layout,
    IReadOnlyList<TankView> Tanks,
    IReadOnlyList<ValveView> Valves,
    IReadOnlyList<WeirView> Weirs,
    double? WwtpInflow,
    bool FieldStale);

/// <summary>
/// Everything the overview display shows, taken at one moment. Drawing is the network as the
/// simulator published it: the geometry of the diagram and the town of the Live view.
/// </summary>
public sealed record HmiView(
    SiteSnapshot Site,
    NetworkDescription? Drawing,
    ScenarioStatus Scenario,
    OverflowKpi Kpi,
    IReadOnlyList<Alarm> Alarms,
    TrendSeries Trend,
    DiagramView? Diagram)
{
    public ControlMode? ModeActive => Site.Plc is Presence.Online ? Site.PlcValues.ModeActive?.Value : null;

    public double? Rain => Site.Sim is Presence.Online ? Site.FieldValues.Rain : null;

    public StormWindow? Storm => Scenario.Catalog?.Storm(Scenario.Storm);

    public ControlAlgorithm? Algorithm => Scenario.Catalog?.Algorithm(Scenario.Algorithm);

    /// <summary>The PLC is asked to take the center's proposals (mode 3).</summary>
    public bool CenterAsked => Site.PlcValues.CmdMode?.Value is ControlMode.EfdCenter;

    /// <summary>The PLC reports that its stations fell back to local control.</summary>
    public bool PlcFallback => (Site.PlcValues.Status ?? PlcStatus.None).HasFlag(PlcStatus.Fallback);
}

public readonly record struct FeedVersion(long Uns, long Alarms, long Trend, bool Broker);

/// <summary>
/// Builds the overview display from the read models. Displays poll <see cref="Version"/> and
/// rebuild only when something changed; the diagram geometry is computed once per network.
/// </summary>
public sealed class HmiFeed(UnsSiteReader site, AlarmEngine alarms, RunTrend trend, IBrokerConnection broker)
{
    private const int TrendPoints = 720;
    private readonly Lock _gate = new();
    private NetworkDescription? _layoutSource;
    private DiagramLayout? _layout;

    public FeedVersion Version => new(site.Version, alarms.Version, trend.Version, broker.IsConnected);

    public HmiView Build()
    {
        var snapshot = site.Read();
        var drawing = site.Description;
        var current = alarms.Current();
        return new HmiView(
            snapshot,
            drawing,
            ScenarioStatus.From(snapshot),
            SiteKpi.Overflow(snapshot),
            current,
            trend.Read(TrendPoints),
            Diagram(snapshot, drawing, current));
    }

    private DiagramView? Diagram(SiteSnapshot s, NetworkDescription? drawing, IReadOnlyList<Alarm> current)
    {
        if (drawing is null || s.Network is not { } network)
        {
            return null;
        }
        DiagramLayout layout;
        lock (_gate)
        {
            if (!ReferenceEquals(drawing, _layoutSource))
            {
                _layoutSource = drawing;
                _layout = DiagramLayout.From(drawing);
            }
            layout = _layout!;
        }

        var plc = s.PlcValues;
        var field = s.FieldValues;
        var plcStale = s.Plc is not Presence.Online;
        var fieldStale = s.Sim is not Presence.Online;
        var alarmById = current.ToDictionary(a => a.Definition.Id);
        Alarm? OverflowAlarm(OverflowPointId point) => alarmById.GetValueOrDefault(AlarmDefinitions.OverflowId(point));

        Alarm? TankAlarm(TankId tank) => new[] { OverflowAlarm(OverflowPointId.Of(tank)), alarmById.GetValueOrDefault(AlarmDefinitions.LevelFrozenId(tank)) }
            .OfType<Alarm>()
            .OrderBy(a => a.Definition.Priority)
            .FirstOrDefault();

        var tanks = layout.Tanks.Select(t =>
        {
            var tank = new TankId(t.Id);
            var level = plc.Level(tank);
            return new TankView(
                t,
                level?.Value,
                level is { } l ? network.Fill(l.Value).Value : null,
                plcStale || level is not { Good: true },
                !fieldStale && field.OverflowActive(OverflowPointId.Of(tank)) is true,
                TankAlarm(tank),
                tank == field.FrozenSensor && field.TrueLevel(tank) is { } real ? network.Fill(real).Value : null);
        }).ToList();

        var valves = layout.Valves.Select(v =>
        {
            if (!v.Controlled)
            {
                return new ValveView(v, null, null, false);
            }
            var gate = new GateId(v.Id);
            var setpoint = plc.Setpoint(gate);
            return new ValveView(
                v,
                setpoint?.Value.Fraction,
                plc.CenterSetpoint(gate)?.Fraction,
                plcStale || setpoint is not { Good: true },
                fieldStale ? null : field.Opening(gate)?.Fraction,
                alarmById.GetValueOrDefault(AlarmDefinitions.GateDeviationId(gate)));
        }).ToList();

        var weirs = layout.Weirs.Select(w =>
        {
            var point = new OverflowPointId(w.Id);
            return new WeirView(
                w,
                !fieldStale && field.OverflowActive(point) is true,
                field.OverflowVolume(point)?.CubicMeters,
                fieldStale,
                OverflowAlarm(point));
        }).ToList();

        return new DiagramView(layout, tanks, valves, weirs, field.WwtpInflow?.CubicMeters, fieldStale);
    }
}
