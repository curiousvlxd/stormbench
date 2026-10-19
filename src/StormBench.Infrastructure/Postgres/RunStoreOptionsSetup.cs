using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace StormBench.Infrastructure.Postgres;

/// <summary>Takes <see cref="RunStoreOptions"/> from <c>ConnectionStrings:runs</c>, as Aspire names it.</summary>
internal sealed class RunStoreOptionsSetup(IConfiguration configuration) : IConfigureOptions<RunStoreOptions>
{
    public void Configure(RunStoreOptions options) =>
        options.ConnectionString = configuration.GetConnectionString(ScadaDatabase.ConnectionName);
}
