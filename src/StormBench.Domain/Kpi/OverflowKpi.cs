using StormBench.Domain.Network;

namespace StormBench.Domain.Kpi;

/// <summary>How the running strategy compares with the Base Case at the same moment of the storm.</summary>
public enum Comparison
{
    /// <summary>No run yet.</summary>
    NoData,

    /// <summary>At least <see cref="OverflowKpi.SameBand"/> less overflow than the Base Case.</summary>
    Better,

    /// <summary>Within the band (this includes "no overflow in either").</summary>
    Same,

    /// <summary>At least <see cref="OverflowKpi.SameBand"/> more overflow than the Base Case.</summary>
    Worse
}

/// <summary>Overflow of the running storm against the Base Case reference the simulator computes for the same window.</summary>
public sealed record OverflowKpi(
    Volume Total,
    Volume? BaseCase,
    Volume? Delta,
    double? Relative,
    Comparison Comparison,
    IReadOnlyList<OverflowPointId> Overflowing,
    int Points,
    Volume? WwtpInflow)
{
    /// <summary>Relative band around the Base Case that counts as the same result.</summary>
    public const double SameBand = 0.01;

    /// <summary>Below this volume both results count as "no overflow".</summary>
    public static Volume NoOverflow { get; } = new(1.0);

    /// <summary>
    /// The comparison once a run has started and reports its total; without a run, a total or a
    /// Base Case there is nothing to compare.
    /// </summary>
    public static OverflowKpi Compare(
        bool runStarted, Volume? total, Volume? baseCase, IReadOnlyList<OverflowPointId> overflowing, int points, Volume? wwtpInflow)
    {
        if (!runStarted || total is not { } spilled)
        {
            return new OverflowKpi(Volume.Zero, null, null, null, Comparison.NoData, overflowing, points, wwtpInflow);
        }
        if (baseCase is not { } reference)
        {
            return new OverflowKpi(spilled, null, null, null, Comparison.NoData, overflowing, points, wwtpInflow);
        }
        var delta = spilled - reference;
        double? relative = reference >= NoOverflow ? spilled.RelativeTo(reference) : null;
        var comparison = reference < NoOverflow
            ? spilled < NoOverflow ? Comparison.Same : Comparison.Worse
            : relative <= -SameBand ? Comparison.Better
            : relative >= SameBand ? Comparison.Worse
            : Comparison.Same;
        return new OverflowKpi(spilled, reference, delta, relative, comparison, overflowing, points, wwtpInflow);
    }
}
