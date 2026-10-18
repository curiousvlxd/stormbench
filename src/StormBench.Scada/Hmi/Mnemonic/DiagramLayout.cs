using StormBench.Contracts;

namespace StormBench.Scada.Hmi.Mnemonic;

public readonly record struct Point2(double X, double Y);

/// <summary>Where a block of text sits: the first baseline and the SVG text-anchor.</summary>
public readonly record struct TextSpot(double X, double Y, string Anchor);

public sealed record PipeShape(string Id, Point2 From, Point2 To, bool IsOutlet);

public sealed record TankShape(string Id, Point2 Center, double Width, double Height, TextSpot Tag, TextSpot Values, Point2 Indicator);

public sealed record ValveShape(string Id, string Tank, Point2 Center, double AngleDegrees, bool Controlled, TextSpot Label);

public sealed record WeirShape(string Id, Point2 Center, TextSpot Label, Point2 Indicator);

public sealed record OutfallShape(string Id, Point2 Center, double Width, double Height);

/// <summary>
/// Geometry of the overview display, computed once per network description: the model's map
/// coordinates with the y axis turned downwards, tanks as vessels, the controlled and fixed tank
/// outlets as valves, overflow structures without storage as weirs. Text is placed automatically
/// where it crosses the fewest pipes and symbols, so another network needs no hand layout.
/// Units are map units.
/// </summary>
public sealed record DiagramLayout(
    double Width,
    double Height,
    IReadOnlyList<PipeShape> Pipes,
    IReadOnlyList<Point2> Joints,
    IReadOnlyList<TankShape> Tanks,
    IReadOnlyList<ValveShape> Valves,
    IReadOnlyList<WeirShape> Weirs,
    OutfallShape? Outfall)
{
    public const double TankWidth = 420;
    public const double TankHeight = 640;
    public const double WeirSize = 240;
    public const double IndicatorSize = 250;

    public const double TagSize = 190;
    public const double ValueSize = 175;
    public const double SubSize = 140;
    public const double LineGap = 160;
    private const double CharWidth = 0.58;

    private const double PadLeft = 1000;
    private const double PadRight = 1000;
    private const double PadTop = 1150;
    private const double PadBottom = 800;
    private const double ValveGap = 260;
    private const double PipeHalfWidth = 30;

    public static DiagramLayout From(NetworkDescription network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var nodes = network.Nodes.ToDictionary(n => n.Id);
        var minX = network.Nodes.Min(n => n.X);
        var maxX = network.Nodes.Max(n => n.X);
        var minY = network.Nodes.Min(n => n.Y);
        var maxY = network.Nodes.Max(n => n.Y);
        var width = maxX - minX + PadLeft + PadRight;
        var height = maxY - minY + PadTop + PadBottom;
        var positions = network.Nodes.ToDictionary(n => n.Id, n => new Point2(n.X - minX + PadLeft, maxY - n.Y + PadTop));
        Point2 At(string id) => positions[id];

        var tankIds = network.Tanks.Where(nodes.ContainsKey).ToList();
        var tankSet = tankIds.ToHashSet();
        var controlled = network.Stations.Select(s => s.Valve).ToHashSet();
        var weirIds = network.OverflowPoints.Where(p => !tankSet.Contains(p) && nodes.ContainsKey(p)).ToList();

        foreach (var weir in weirIds)
        {
            foreach (var tank in tankIds)
            {
                positions[weir] = Separate(positions[weir], positions[tank], (WeirSize + TankWidth) / 2 + 80, (WeirSize + TankHeight) / 2 + 80);
            }
        }

        List<PipeShape> pipes = [];
        List<(string Id, string Tank, Point2 From, Point2 To)> outlets = [];
        foreach (var link in network.Links.Where(l => nodes.ContainsKey(l.From) && nodes.ContainsKey(l.To)))
        {
            var isOutlet = link.Kind == "orifice" && tankSet.Contains(link.From);
            pipes.Add(new PipeShape(link.Id, At(link.From), At(link.To), isOutlet));
            if (isOutlet)
            {
                outlets.Add((link.Id, link.From, At(link.From), At(link.To)));
            }
        }

        var scene = new Scene(width, height, pipes);

        foreach (var tank in tankIds)
        {
            scene.Block(Box.Around(At(tank), TankWidth, TankHeight));
        }
        foreach (var weir in weirIds)
        {
            scene.Block(Box.Around(At(weir), WeirSize, WeirSize));
        }
        OutfallShape? outfall = nodes.ContainsKey(network.Wwtp) ? new OutfallShape(network.Wwtp, At(network.Wwtp), 640, 400) : null;
        if (outfall is not null)
        {
            scene.Block(Box.Around(outfall.Center, outfall.Width, outfall.Height + 460));
        }
        var valveCenters = outlets.Select(o => ValveOnPipe(o.From, o.To)).ToList();
        foreach (var (center, _) in valveCenters)
        {
            scene.Block(Box.Around(center, 240, 240));
        }

        Dictionary<string, TextSpot> tagSpots = [];
        Dictionary<string, Point2> tankIndicators = [];
        foreach (var tank in tankIds)
        {
            var c = At(tank);
            var tag = new TextSpot(c.X, c.Y - (TankHeight / 2) - 90, "middle");
            tagSpots[tank] = tag;
            scene.Block(TextBox(tag, [(tank, TagSize)]));
        }
        foreach (var tank in tankIds)
        {
            var c = At(tank);
            var tagY = tagSpots[tank].Y - (TagSize * 0.35);
            var half = TextWidth(tank, TagSize) / 2;
            tankIndicators[tank] = scene.PlacePoint(IndicatorSize,
            [
                new Point2(c.X - half - 190, tagY),
                new Point2(c.X + half + 190, tagY),
                new Point2(c.X - (TankWidth / 2) - 190, c.Y - (TankHeight / 2) + 140),
                new Point2(c.X + (TankWidth / 2) + 190, c.Y + (TankHeight / 2) - 140),
            ]);
        }

        List<TankShape> tanks = [];
        foreach (var tank in tankIds)
        {
            var c = At(tank);
            var x0 = c.X - (TankWidth / 2);
            var x1 = c.X + (TankWidth / 2);
            var y0 = c.Y - (TankHeight / 2);
            var y1 = c.Y + (TankHeight / 2);
            (string, double)[] lines = [("100% ?", ValueSize), ("4.00 m", SubSize), ("real 100%", SubSize)];
            var spot = scene.Place(lines,
            [
                new TextSpot(x1 + 70, y0 + ValueSize + 10, "start"),
                new TextSpot(x0 - 70, y0 + ValueSize + 10, "end"),
                new TextSpot(x1 + 70, y1 - LineGap - 20, "start"),
                new TextSpot(x0 - 70, y1 - LineGap - 20, "end"),
                new TextSpot(c.X, y1 + ValueSize + 70, "middle"),
            ]);
            tanks.Add(new TankShape(tank, c, TankWidth, TankHeight, tagSpots[tank], spot, tankIndicators[tank]));
        }

        List<ValveShape> valves = [];
        for (var i = 0; i < outlets.Count; i++)
        {
            var (id, tank, _, _) = outlets[i];
            var (center, angle) = valveCenters[i];
            (string, double)[] lines = [(id, SubSize), ("100%", 150)];
            var spot = scene.Place(lines,
            [
                new TextSpot(center.X + 170, center.Y + 40, "start"),
                new TextSpot(center.X - 170, center.Y + 40, "end"),
                new TextSpot(center.X, center.Y + 150 + SubSize, "middle"),
                new TextSpot(center.X, center.Y - 170 - LineGap, "middle"),
                new TextSpot(center.X + 150, center.Y + 150 + SubSize, "start"),
                new TextSpot(center.X - 150, center.Y + 150 + SubSize, "end"),
            ]);
            valves.Add(new ValveShape(id, tank, center, angle, controlled.Contains(id), spot));
        }

        List<WeirShape> weirs = [];
        foreach (var weir in weirIds)
        {
            var c = At(weir);
            (string, double)[] lines = [(weir, 160), ("9,999 m³", SubSize)];
            List<TextSpot> candidates = [];
            if (network.LabelOffsets?.GetValueOrDefault(weir) is [var dx, var dy])
            {
                candidates.Add(new TextSpot(c.X + dx, c.Y - dy + 160, "middle"));
            }
            candidates.AddRange(
            [
                new TextSpot(c.X, c.Y + (WeirSize / 2) + 200, "middle"),
                new TextSpot(c.X + (WeirSize / 2) + 70, c.Y - 20, "start"),
                new TextSpot(c.X - (WeirSize / 2) - 70, c.Y - 20, "end"),
                new TextSpot(c.X, c.Y - (WeirSize / 2) - 80 - LineGap, "middle"),
            ]);
            var indicator = scene.PlacePoint(IndicatorSize,
            [
                new Point2(c.X - (WeirSize / 2) - 170, c.Y),
                new Point2(c.X + (WeirSize / 2) + 170, c.Y),
                new Point2(c.X - (WeirSize / 2) - 170, c.Y + 260),
                new Point2(c.X + (WeirSize / 2) + 170, c.Y + 260),
                new Point2(c.X, c.Y + (WeirSize / 2) + 200),
            ]);
            var spot = scene.Place(lines, [.. candidates]);
            weirs.Add(new WeirShape(weir, c, spot, indicator));
        }

        var joints = network.Nodes
            .Where(n => n.Kind == "junction" && !network.OverflowPoints.Contains(n.Id))
            .Select(n => At(n.Id))
            .ToList();

        return new DiagramLayout(width, height, pipes, joints, tanks, valves, weirs, outfall);
    }

    public static double TextWidth(string text, double size) => text.Length * size * CharWidth;

    /// <summary>Moves <paramref name="point"/> away from <paramref name="anchor"/> until their boxes keep the given distances.</summary>
    private static Point2 Separate(Point2 point, Point2 anchor, double minDx, double minDy)
    {
        var dx = point.X - anchor.X;
        var dy = point.Y - anchor.Y;
        if (Math.Abs(dx) >= minDx || Math.Abs(dy) >= minDy)
        {
            return point;
        }
        var outX = (minDx - Math.Abs(dx)) * (dx >= 0 ? 1 : -1);
        var outY = (minDy - Math.Abs(dy)) * (dy >= 0 ? 1 : -1);
        return Math.Abs(outX) <= Math.Abs(outY) ? point with { X = point.X + outX } : point with { Y = point.Y + outY };
    }

    /// <summary>The box a block of text lines occupies, from the first baseline down.</summary>
    private static Box TextBox(TextSpot spot, (string Text, double Size)[] lines)
    {
        var width = lines.Max(l => TextWidth(l.Text, l.Size));
        var top = spot.Y - (lines[0].Size * 0.8);
        var bottom = spot.Y + ((lines.Length - 1) * LineGap) + (lines[^1].Size * 0.25);
        var left = spot.Anchor switch
        {
            "end" => spot.X - width,
            "middle" => spot.X - (width / 2),
            _ => spot.X,
        };
        return new Box(left, top, left + width, bottom);
    }

    /// <summary>A valve on the tank's outlet pipe, just outside the vessel, turned along the pipe.</summary>
    private static (Point2 Center, double Angle) ValveOnPipe(Point2 tank, Point2 downstream)
    {
        var dx = downstream.X - tank.X;
        var dy = downstream.Y - tank.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
        {
            return (tank, 0);
        }
        var ux = dx / length;
        var uy = dy / length;
        var toEdge = Math.Min(
            Math.Abs(ux) > 1e-9 ? TankWidth / 2 / Math.Abs(ux) : double.MaxValue,
            Math.Abs(uy) > 1e-9 ? TankHeight / 2 / Math.Abs(uy) : double.MaxValue);
        var distance = Math.Min(toEdge + ValveGap, length * 0.6);
        return (new Point2(tank.X + (ux * distance), tank.Y + (uy * distance)), Math.Atan2(dy, dx) * 180.0 / Math.PI);
    }

    private readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public static Box Around(Point2 c, double w, double h) => new(c.X - (w / 2), c.Y - (h / 2), c.X + (w / 2), c.Y + (h / 2));

        public double Overlap(Box o) =>
            Math.Max(0, Math.Min(X1, o.X1) - Math.Max(X0, o.X0)) * Math.Max(0, Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0));

        public Box Inflate(double d) => new(X0 - d, Y0 - d, X1 + d, Y1 + d);

        /// <summary>Liang-Barsky: does the segment cross this box?</summary>
        public bool Crosses(Point2 a, Point2 b)
        {
            double t0 = 0, t1 = 1;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            foreach (var (p, q) in new[] { (-dx, a.X - X0), (dx, X1 - a.X), (-dy, a.Y - Y0), (dy, Y1 - a.Y) })
            {
                if (Math.Abs(p) < 1e-12)
                {
                    if (q < 0)
                    {
                        return false;
                    }
                    continue;
                }
                var t = q / p;
                if (p < 0)
                {
                    t0 = Math.Max(t0, t);
                }
                else
                {
                    t1 = Math.Min(t1, t);
                }
                if (t0 > t1)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Greedy label placement: each block takes the first candidate with the lowest cost.</summary>
    private sealed class Scene(double width, double height, IReadOnlyList<PipeShape> pipes)
    {
        private readonly List<Box> _taken = [];

        public void Block(Box box) => _taken.Add(box);

        public TextSpot Place((string Text, double Size)[] lines, TextSpot[] candidates)
        {
            var best = candidates[0];
            var bestCost = double.MaxValue;
            for (var i = 0; i < candidates.Length; i++)
            {
                var cost = Cost(TextBox(candidates[i], lines)) + i;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = candidates[i];
                }
            }
            _taken.Add(TextBox(best, lines));
            return best;
        }

        public Point2 PlacePoint(double size, Point2[] candidates)
        {
            var best = candidates.MinBy(c => Cost(Box.Around(c, size, size)));
            _taken.Add(Box.Around(best, size, size));
            return best;
        }

        private double Cost(Box box)
        {
            var cost = 0.0;
            var padded = box.Inflate(PipeHalfWidth);
            foreach (var pipe in pipes)
            {
                if (padded.Crosses(pipe.From, pipe.To))
                {
                    cost += 10_000;
                }
            }
            foreach (var taken in _taken)
            {
                cost += taken.Inflate(40).Overlap(box);
            }
            if (box.X0 < 0 || box.Y0 < 0 || box.X1 > width || box.Y1 > height)
            {
                cost += 50_000;
            }
            return cost;
        }
    }
}
