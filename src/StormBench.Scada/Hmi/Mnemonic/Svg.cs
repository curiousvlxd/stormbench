using System.Globalization;

namespace StormBench.Scada.Hmi.Mnemonic;

/// <summary>SVG attribute values: invariant culture, one decimal at most.</summary>
public static class Svg
{
    public static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Points(IEnumerable<Point2> points) => string.Join(' ', points.Select(p => $"{N(p.X)},{N(p.Y)}"));
}
