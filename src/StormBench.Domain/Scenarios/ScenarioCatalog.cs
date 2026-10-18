using System.Diagnostics.CodeAnalysis;
using StormBench.Domain.Control;

namespace StormBench.Domain.Scenarios;

/// <summary>A storm of the catalog, e.g. 2000-05-30.</summary>
public readonly record struct StormId(string Value) : IParsable<StormId>
{
    public override string ToString() => Value;

    public static StormId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StormId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}

/// <summary>A control algorithm of the catalog, e.g. efd-center.</summary>
public readonly record struct AlgorithmId(string Value) : IParsable<AlgorithmId>
{
    public override string ToString() => Value;

    public static AlgorithmId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AlgorithmId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}

/// <summary>A storm window in model time (the model has no time zone) and its number of control steps.</summary>
public sealed record StormWindow(StormId Id, string Label, string Description, DateTime Start, DateTime End, long Steps);

/// <summary>
/// A control algorithm: the PLC's control mode for it and, in mode 3, the center that proposes
/// the setpoints; null for algorithms that run in the PLC alone.
/// </summary>
public sealed record ControlAlgorithm(AlgorithmId Id, string Label, string Description, ControlMode Mode, ControlCenter? Center);

/// <summary>The storms, control algorithms and paces the test console offers.</summary>
public sealed record ScenarioCatalog(
    IReadOnlyList<StormWindow> Storms,
    IReadOnlyList<ControlAlgorithm> Algorithms,
    IReadOnlyList<double> Paces,
    StormId DefaultStorm,
    AlgorithmId DefaultAlgorithm,
    double DefaultPace)
{
    public StormWindow? Storm(StormId? id) => Storms.FirstOrDefault(s => s.Id == id);

    public ControlAlgorithm? Algorithm(AlgorithmId? id) => Algorithms.FirstOrDefault(a => a.Id == id);
}

/// <summary>Wall seconds per control step the test console accepts: 0 runs as fast as the PLC answers.</summary>
public static class StormPace
{
    public const double MaxSeconds = 5.0;

    public static bool IsValid(double seconds) => seconds is >= 0.0 and <= MaxSeconds;
}
