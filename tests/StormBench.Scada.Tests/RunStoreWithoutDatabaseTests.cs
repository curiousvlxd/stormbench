using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormBench.Application.Alarms;
using StormBench.Application.Runs;
using StormBench.Infrastructure.Postgres;

namespace StormBench.Scada.Tests;

/// <summary>Without <c>ConnectionStrings:runs</c> the stores keep nothing and say so when read.</summary>
public sealed class RunStoreWithoutDatabaseTests
{
    [Fact]
    public async Task Nothing_is_kept_and_reading_says_there_is_no_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var database = new ScadaDatabase(Options.Create(new RunStoreOptions()), NullLogger<ScadaDatabase>.Instance);
        var runs = new PostgresRunStore(database, NullLogger<PostgresRunStore>.Instance);
        var journal = new PostgresAlarmJournal(database, NullLogger<PostgresAlarmJournal>.Instance);

        Assert.False(database.Enabled);
        await Assert.ThrowsAsync<StoreUnavailableException>(() => runs.ListAsync(10, ct));
        await Assert.ThrowsAsync<StoreUnavailableException>(() => journal.ReadAsync(null, 10, ct));
    }
}
