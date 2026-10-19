using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StormBench.Contracts;
using StormBench.Sparkplug.HostApplication;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>
/// Binds <see cref="HostApplicationOptions"/> to the broker section <c>Mqtt</c>; the host id and
/// the group default to the UNS contract's.
/// </summary>
internal sealed class HostApplicationOptionsSetup(IConfiguration configuration) : IConfigureOptions<HostApplicationOptions>
{
    public const string Section = "Mqtt";

    public void Configure(HostApplicationOptions options)
    {
        configuration.GetSection(Section).Bind(options);
        options.HostId = string.IsNullOrEmpty(options.HostId) ? Uns.PrimaryHostId : options.HostId;
        options.GroupId = string.IsNullOrEmpty(options.GroupId) ? Uns.GroupId : options.GroupId;
    }
}
