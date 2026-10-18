using StormBench.Contracts;
using StormBench.Scada.Hmi.Mnemonic;

namespace StormBench.Scada.Tests;

public class DiagramLayoutTests
{
    private static readonly DiagramLayout Layout = DiagramLayout.From(NetworkDescription.Parse(SiteFixture.NetworkJson)!);

    private readonly record struct Rect(double X0, double Y0, double X1, double Y1)
    {
        public static Rect Around(Point2 c, double w, double h) => new(c.X - (w / 2), c.Y - (h / 2), c.X + (w / 2), c.Y + (h / 2));

        public bool Overlaps(Rect o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    [Fact]
    public void Map_y_axis_points_down_and_everything_fits_the_canvas()
    {
        var t1 = Layout.Tanks.Single(t => t.Id == "T1");
        var weir = Layout.Weirs.Single();
        Assert.True(weir.Center.Y < t1.Center.Y, "CSO1 lies north of T1 in the model, so above it on screen");
        Assert.All(Layout.Tanks, t => Assert.InRange(t.Center.X, 0, Layout.Width));
        Assert.All(Layout.Tanks, t => Assert.InRange(t.Center.Y, 0, Layout.Height));
    }

    [Fact]
    public void Overflow_structure_too_close_to_a_tank_is_moved_clear_and_its_pipe_follows()
    {
        var t1 = Layout.Tanks.Single(t => t.Id == "T1");
        var weir = Layout.Weirs.Single();

        var tankBox = Rect.Around(t1.Center, t1.Width, t1.Height);
        var weirBox = Rect.Around(weir.Center, DiagramLayout.WeirSize, DiagramLayout.WeirSize);
        Assert.False(tankBox.Overlaps(weirBox));
        Assert.Equal(weir.Center, Layout.Pipes.Single(p => p.Id == "C2").From);
    }

    [Fact]
    public void Tank_outlets_become_valves_on_their_pipes_controlled_or_fixed()
    {
        var v2 = Layout.Valves.Single(v => v.Id == "V2");
        var v1 = Layout.Valves.Single(v => v.Id == "V1");

        Assert.True(v2.Controlled);
        Assert.False(v1.Controlled);
        Assert.Equal("T2", v2.Tank);
        var t2 = Layout.Tanks.Single(t => t.Id == "T2");
        Assert.False(Rect.Around(t2.Center, t2.Width, t2.Height).Overlaps(Rect.Around(v2.Center, 200, 156)));
        Assert.Equal(0, v2.AngleDegrees, 6);
    }

    [Fact]
    public void Text_is_placed_off_the_pipes_and_off_the_vessels()
    {
        foreach (var tank in Layout.Tanks)
        {
            var values = TextRect(tank.Values, "100% ?", DiagramLayout.ValueSize);
            Assert.All(Layout.Tanks, other => Assert.False(values.Overlaps(Rect.Around(other.Center, other.Width, other.Height)), $"{tank.Id} values over {other.Id}"));
        }
        foreach (var valve in Layout.Valves)
        {
            var label = TextRect(valve.Label, valve.Id, DiagramLayout.SubSize);
            Assert.All(Layout.Tanks, t => Assert.False(label.Overlaps(Rect.Around(t.Center, t.Width, t.Height)), $"{valve.Id} label over {t.Id}"));
        }
    }

    [Fact]
    public void Label_offset_from_the_model_is_the_first_choice()
    {
        var weir = Layout.Weirs.Single();

        Assert.Equal("middle", weir.Label.Anchor);
        Assert.True(weir.Label.Y < weir.Center.Y, "offset (0, 700) in map units puts the name above the symbol");
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var again = DiagramLayout.From(NetworkDescription.Parse(SiteFixture.NetworkJson)!);

        Assert.Equal(Layout.Tanks, again.Tanks);
        Assert.Equal(Layout.Valves, again.Valves);
        Assert.Equal(Layout.Weirs, again.Weirs);
    }

    private static Rect TextRect(TextSpot spot, string text, double size)
    {
        var width = DiagramLayout.TextWidth(text, size);
        var left = spot.Anchor switch
        {
            "end" => spot.X - width,
            "middle" => spot.X - (width / 2),
            _ => spot.X,
        };
        return new Rect(left, spot.Y - size, left + width, spot.Y + DiagramLayout.LineGap);
    }
}
