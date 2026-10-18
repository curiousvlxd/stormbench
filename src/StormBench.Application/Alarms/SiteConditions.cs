using StormBench.Application.Site;
using StormBench.Domain.Alarms;
using StormBench.Domain.Control;

namespace StormBench.Application.Alarms;

/// <summary>
/// Alarm conditions of the Astlingen site, evaluated from the UNS after every message. A
/// condition is only evaluated on good data: stale values neither raise nor clear an alarm, the
/// communication alarms say that the data are stale.
/// </summary>
public static class SiteConditions
{
    public static IEnumerable<(AlarmDefinition Definition, bool Active)> Evaluate(SiteSnapshot site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return EvaluateCore(site);
    }

    private static IEnumerable<(AlarmDefinition Definition, bool Active)> EvaluateCore(SiteSnapshot site)
    {
        if (!site.BrokerConnected)
        {
            if (site.BrokerSeen)
            {
                yield return (AlarmDefinitions.Broker, true);
            }
            yield break;
        }
        yield return (AlarmDefinitions.Broker, false);
        yield return (AlarmDefinitions.EdgeOffline, site.Edge is Presence.Offline);
        yield return (AlarmDefinitions.PlcOffline, site.Plc is Presence.Offline && site.Edge is Presence.Online);
        yield return (AlarmDefinitions.SimOffline, site.Sim is Presence.Offline);

        if (site.Plc is Presence.Online && site.PlcValues.Status is { } status)
        {
            yield return (AlarmDefinitions.InputFault, status.HasFlag(PlcStatus.InputFault));
            yield return (AlarmDefinitions.Fallback, status.HasFlag(PlcStatus.Fallback));
            yield return (AlarmDefinitions.BadProposal, status.HasFlag(PlcStatus.BadProposal));
            yield return (AlarmDefinitions.Clamped, status.HasFlag(PlcStatus.Clamped));
        }

        if (site.Sim is Presence.Online && site.Network is { } network)
        {
            foreach (var point in network.OverflowPoints)
            {
                if (site.FieldValues.OverflowActive(point.Id) is { } active)
                {
                    yield return (AlarmDefinitions.Overflow(point), active);
                }
            }
        }
    }
}
