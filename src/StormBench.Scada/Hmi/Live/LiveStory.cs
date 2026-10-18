using System.Globalization;
using System.Text.RegularExpressions;
using StormBench.Application.Site;
using StormBench.Domain.Alarms;
using StormBench.Domain.Kpi;
using StormBench.Domain.Scenarios;

namespace StormBench.Scada.Hmi.Live;

public enum Tone
{
    Info,
    Good,
    Warning,
    Bad
}

public sealed record StoryLine(string Text, Tone Tone);

/// <summary>
/// The Live view's narrator: what is happening now, in words a visitor understands without
/// knowing SCADA or the benchmark. Most important first; the view shows the first few.
/// </summary>
public static partial class LiveStory
{
    public const double BathtubM3 = 0.15;
    public const double PoolM3 = 2500;

    /// <summary>Below this fill a level that stays put is a dry tank to the SCADA's frozen-sensor check.</summary>
    public const double EmptyFill = 0.02;

    public static IReadOnlyList<StoryLine> Tell(HmiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var s = view.Scenario;
        List<StoryLine> lines = [];
        if (s.Simulator is not Presence.Online)
        {
            lines.Add(new("Simulator offline", Tone.Warning));
            return lines;
        }
        if (view.Site.Plc is Presence.Offline)
        {
            lines.Add(new("PLC does not answer", Tone.Bad));
        }

        var link = LiveFrame.LinkState(view);
        if (link is "cut" or "lost")
        {
            lines.Add(LiveFrame.LocalFallback(view)
                ? new("Link cut: the PLC carries on alone", Tone.Warning)
                : new("Link cut", Tone.Warning));
        }

        var alarms = view.Alarms.Where(a => a.Active).Select(a => a.Definition.Id).ToHashSet();
        if (s.StuckGate is { } gate)
        {
            lines.Add(alarms.Contains(AlarmDefinitions.GateDeviationId(gate))
                ? new($"{Plain(gate.Value)} jammed: SCADA caught it", Tone.Bad)
                : new($"{Plain(gate.Value)} jammed: SCADA checking...", Tone.Warning));
        }
        if (s.FrozenSensor is { } tank)
        {
            var reading = view.Diagram?.Tanks.FirstOrDefault(t => t.Shape.Id == tank.Value)?.Fill;
            lines.Add(alarms.Contains(AlarmDefinitions.LevelFrozenId(tank))
                ? new($"{Plain(tank.Value)} sensor frozen: SCADA caught it", Tone.Bad)
                : reading is < EmptyFill
                    ? new($"{Plain(tank.Value)} sensor frozen while empty: SCADA cannot tell", Tone.Warning)
                    : new($"{Plain(tank.Value)} sensor frozen: SCADA checking...", Tone.Warning));
        }

        switch (s.State)
        {
            case ScenarioState.Preparing:
                lines.Add(new("Preparing the storm...", Tone.Info));
                break;
            case ScenarioState.Running:
                var spilling = view.Kpi.Overflowing;
                if (spilling.Count > 2)
                {
                    lines.Add(new($"{spilling.Count} places spill into the river", Tone.Bad));
                }
                else if (spilling.Count > 0)
                {
                    lines.Add(new($"{Join(spilling.Select(p => Plain(p.Value)))} {(spilling.Count == 1 ? "spills" : "spill")} into the river", Tone.Bad));
                }
                else
                {
                    lines.Add(view.Rain is > 0.05 and var rain
                        ? new($"Raining {rain.ToString("0.0", CultureInfo.InvariantCulture)} mm/h: tanks filling", Tone.Info)
                        : new("Dry: tanks emptying", Tone.Info));
                }
                break;
            case ScenarioState.Finished:
                lines.Add(Verdict(view.Kpi, finished: true));
                break;
            case ScenarioState.Failed:
                lines.Add(new("Run failed", Tone.Bad));
                break;
            default:
                lines.Add(new("Press Start storm", Tone.Info));
                break;
        }
        return lines;
    }

    /// <summary>The result against the same storm without smart control.</summary>
    public static StoryLine Verdict(OverflowKpi kpi, bool finished)
    {
        ArgumentNullException.ThrowIfNull(kpi);
        var prefix = finished ? "Done: " : "";
        return kpi.Comparison switch
        {
            Comparison.Better when kpi.Relative is { } r =>
                new($"{prefix}{OneDecimal(-r)} less into the river", Tone.Good),
            Comparison.Worse when kpi.Relative is { } r =>
                new($"{prefix}{OneDecimal(r)} more into the river", Tone.Bad),
            Comparison.Worse => new($"{prefix}more into the river", Tone.Bad),
            Comparison.Same when kpi.Total < OverflowKpi.NoOverflow =>
                new($"{prefix}nothing spilled", Tone.Good),
            Comparison.Same => new($"{prefix}same as no control", Tone.Info),
            _ => new($"{prefix}no comparison", Tone.Info),
        };
    }

    private static string OneDecimal(double fraction) => $"{(fraction * 100).ToString("0.#", CultureInfo.InvariantCulture)}%";

    /// <summary>A volume in things people can picture: bathtubs, or Olympic pools when it is large.</summary>
    public static string Picture(double m3)
    {
        var inv = CultureInfo.InvariantCulture;
        if (m3 >= PoolM3 * 0.95)
        {
            var pools = m3 / PoolM3;
            return $"about {pools.ToString(pools < 10 ? "0.#" : "0", inv)} Olympic pools";
        }
        var tubs = m3 / BathtubM3;
        var rounded = tubs >= 1000 ? Math.Round(tubs / 100) * 100 : Math.Round(tubs);
        return $"about {rounded.ToString("N0", inv)} bathtubs";
    }

    /// <summary>Model names in plain words: T4 is Tank 4, CSO7 is Overflow 7, V2 is Gate 2.</summary>
    public static string Plain(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var m = NamePattern().Match(id);
        if (!m.Success)
        {
            return id;
        }
        var kind = m.Groups[1].Value switch
        {
            "T" => "Tank",
            "CSO" => "Overflow",
            _ => "Gate",
        };
        return $"{kind} {m.Groups[2].Value}";
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}",
        };
    }

    [GeneratedRegex("^(T|CSO|V)(\\d+)$")]
    private static partial Regex NamePattern();
}
