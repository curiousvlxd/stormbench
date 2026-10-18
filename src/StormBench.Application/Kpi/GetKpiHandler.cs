using StormBench.Application.Site;
using StormBench.Domain.Kpi;

namespace StormBench.Application.Kpi;

public static class SiteKpi
{
    /// <summary>The overflow of the storm the simulated field reports, against its Base Case.</summary>
    public static OverflowKpi Overflow(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var field = site.FieldValues;
        var points = site.Network?.OverflowPoints ?? [];
        return OverflowKpi.Compare(
            field.Run is > 0,
            field.OverflowTotal,
            field.BaseCaseOverflowTotal,
            [.. points.Where(p => field.OverflowActive(p.Id) is true).Select(p => p.Id)],
            points.Count,
            field.WwtpInflow);
    }
}

/// <summary>Overflow of the running storm against its Base Case.</summary>
public sealed class GetKpiHandler(ISiteReader site)
{
    public OverflowKpi Handle() => SiteKpi.Overflow(site.Read());
}
