using StormBench.Contracts;
using StormBench.Scada.Hmi.Mnemonic;

namespace StormBench.Scada.Tests;

/// <summary>The overview of the real Astlingen network, as the simulator publishes it: nothing written over anything else.</summary>
public class DiagramLayoutAstlingenTests
{
    private static readonly DiagramLayout Layout = DiagramLayout.From(
        NetworkDescription.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "astlingen-network.json")))!);

    private readonly record struct Rect(string Name, double X0, double Y0, double X1, double Y1)
    {
        public static Rect Around(string name, Point2 c, double w, double h) => new(name, c.X - (w / 2), c.Y - (h / 2), c.X + (w / 2), c.Y + (h / 2));

        public bool Overlaps(Rect o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    private static Rect Text(string name, TextSpot spot, params (string Text, double Size)[] lines)
    {
        var width = lines.Max(l => DiagramLayout.TextWidth(l.Text, l.Size));
        var left = spot.Anchor switch
        {
            "end" => spot.X - width,
            "middle" => spot.X - (width / 2),
            _ => spot.X
        };
        return new Rect(name, left, spot.Y - (lines[0].Size * 0.8), left + width, spot.Y + ((lines.Length - 1) * DiagramLayout.LineGap) + (lines[^1].Size * 0.25));
    }

    private static List<Rect> Symbols()
    {
        List<Rect> symbols = [];
        symbols.AddRange(Layout.Tanks.Select(t => Rect.Around(t.Id, t.Center, t.Width + (2 * DiagramLayout.SpillMargin), t.Height)));
        symbols.AddRange(Layout.Weirs.Select(w => Rect.Around(w.Id, w.Center, DiagramLayout.WeirSize + (2 * DiagramLayout.SpillMargin), DiagramLayout.WeirSize)));
        symbols.AddRange(Layout.Valves.Select(v => Rect.Around(v.Id, v.Center, 200, 156)));
        if (Layout.Outfall is { } o)
        {
            symbols.Add(Rect.Around(o.Id, o.Center, o.Width, o.Height));
        }
        return symbols;
    }

    private static List<Rect> Texts()
    {
        List<Rect> texts = [];
        texts.AddRange(Layout.Tanks.Select(t => Text($"{t.Id} tag", t.Tag, (t.Id, DiagramLayout.TagSize))));
        texts.AddRange(Layout.Tanks.Select(t => Text($"{t.Id} values", t.Values, ("100% ?", DiagramLayout.ValueSize), ("4.00 m", DiagramLayout.SubSize), ("real 100%", DiagramLayout.SubSize))));
        texts.AddRange(Layout.Valves.Select(v => Text($"{v.Id} label", v.Label, (v.Id, DiagramLayout.SubSize), ("100%", 150))));
        texts.AddRange(Layout.Weirs.Select(w => Text($"{w.Id} label", w.Label, (w.Id, 160), ("9,999 m³", DiagramLayout.SubSize))));
        texts.AddRange(Layout.Tanks.Select(t => Rect.Around($"{t.Id} alarm", t.Indicator, DiagramLayout.IndicatorSize, DiagramLayout.IndicatorSize)));
        texts.AddRange(Layout.Valves.Select(v => Rect.Around($"{v.Id} alarm", v.Indicator, DiagramLayout.IndicatorSize, DiagramLayout.IndicatorSize)));
        texts.AddRange(Layout.Weirs.Select(w => Rect.Around($"{w.Id} alarm", w.Indicator, DiagramLayout.IndicatorSize, DiagramLayout.IndicatorSize)));
        return texts;
    }

    [Fact]
    public void No_text_or_alarm_mark_lies_over_a_symbol()
    {
        var clashes = Texts().SelectMany(t => Symbols().Where(t.Overlaps).Select(s => $"{t.Name} over {s.Name}")).ToList();
        Assert.Empty(clashes);
    }

    [Fact]
    public void No_two_blocks_of_text_or_alarm_marks_overlap()
    {
        var texts = Texts();
        var clashes = texts.SelectMany((a, i) => texts.Skip(i + 1).Where(a.Overlaps).Select(b => $"{a.Name} and {b.Name}")).ToList();
        Assert.Empty(clashes);
    }
}
