using Microsoft.Extensions.Options;
using StormBench.Contracts;
using StormBench.Sparkplug.EdgeNode;

namespace StormBench.Connectivity.EdgeNode;

/// <summary>
/// Binds <see cref="EdgeNodeOptions"/> to the broker section <c>Mqtt</c>; the group, the node and
/// the Primary Host it waits for default to the UNS contract's.
/// </summary>
internal sealed class EdgeNodeOptionsSetup(IConfiguration configuration) : IConfigureOptions<EdgeNodeOptions>
{
    public const string Section = "Mqtt";

    public void Configure(EdgeNodeOptions options)
    {
        configuration.GetSection(Section).Bind(options);
        options.GroupId = string.IsNullOrEmpty(options.GroupId) ? Uns.GroupId : options.GroupId;
        options.EdgeNodeId = string.IsNullOrEmpty(options.EdgeNodeId) ? Uns.EdgeNodeId : options.EdgeNodeId;
        options.PrimaryHostId ??= Uns.PrimaryHostId;
    }
}
