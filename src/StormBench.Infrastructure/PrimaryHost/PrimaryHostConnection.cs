using StormBench.Application.Site;
using StormBench.Sparkplug.HostApplication;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>The broker connection of the Sparkplug Primary Host session.</summary>
public sealed class PrimaryHostConnection(SparkplugHostApplication host) : IBrokerConnection
{
    public bool IsConnected => host.IsConnected;
}
