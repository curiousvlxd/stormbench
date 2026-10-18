using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StormBench.Application.Alarms;
using StormBench.Domain.Alarms;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;
using StormBench.Infrastructure.Postgres;

namespace StormBench.Scada.Tests;

/// <summary>Run summaries and the alarm journal against a real PostgreSQL 17 (Testcontainers).</summary>
public sealed class RunStoreTests : IAsyncLifetime
{
    private const string Image =
        "postgres:17.11-alpine@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24";

    private static readonly DateTimeOffset Started = new(2026, 10, 17, 14, 25, 30, TimeSpan.Zero);

    private readonly IContainer _postgres = new ContainerBuilder(Image)
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .WithEnvironment("POSTGRES_DB", "runs")
        .WithPortBinding(5432, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "postgres", "-d", "runs"))
        .Build();

    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(
            $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Username=postgres;Password=test;Database=runs");
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ScadaDatabase Database() => new(_dataSource, NullLogger<ScadaDatabase>.Instance);

    private static RunSummary Run(string id, RunState state, DateTimeOffset? finished = null, double overflow = 0, double? baseCase = null)
    {
        Volume? reference = baseCase is { } b ? new Volume(b) : null;
        return new(
            new RunId(id), 3, new StormId("2000-05-30"), "30 May 2000", new AlgorithmId("efd-center"), "EFD from the center", 0.1, state,
            new DateTime(2000, 5, 30, 0, 0, 0), new DateTime(2000, 5, 31, 12, 0, 0), Started, finished,
            431, 431, new Volume(overflow), reference, RunSummary.Change(new Volume(overflow), reference), new Volume(52_000.5), 0);
    }

    [Fact]
    public async Task A_run_is_one_row_updated_at_its_end()
    {
        var ct = TestContext.Current.CancellationToken;
        using var database = Database();
        var store = new PostgresRunStore(database, NullLogger<PostgresRunStore>.Instance);

        store.Save(Run("r1", RunState.Running));
        await store.Writer.FlushAsync(ct);
        store.Save(Run("r1", RunState.Finished, Started.AddMinutes(1), 11_967.0, 12_931.0));
        await store.Writer.FlushAsync(ct);

        var run = Assert.Single(await store.ListAsync(10, ct));
        Assert.Equal(Run("r1", RunState.Finished, Started.AddMinutes(1), 11_967.0, 12_931.0), run);
        Assert.Equal(0, store.Writer.Failed);
    }

    [Fact]
    public async Task Runs_left_open_by_a_previous_Scada_are_interrupted()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var before = Database())
        {
            var store = new PostgresRunStore(before, NullLogger<PostgresRunStore>.Instance);
            store.Save(Run("open", RunState.Running));
            store.Save(Run("done", RunState.Finished, Started.AddMinutes(1)));
            await store.Writer.FlushAsync(ct);
        }

        using var after = Database();
        var runs = await new PostgresRunStore(after, NullLogger<PostgresRunStore>.Instance).ListAsync(10, ct);

        Assert.Equal(RunState.Interrupted, runs.Single(r => r.Id.Value == "open").State);
        Assert.Equal(RunState.Finished, runs.Single(r => r.Id.Value == "done").State);
    }

    [Fact]
    public async Task The_journal_returns_the_transitions_of_a_run_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        using var database = Database();
        var journal = new PostgresAlarmJournal(database, NullLogger<PostgresAlarmJournal>.Instance);
        var sim = new DateTime(2000, 5, 30, 22, 25, 0);
        JournalEntry Entry(string? run, AlarmState from, AlarmState to, int seconds) => new(
            0, run is null ? null : new RunId(run), "T1.Overflow", "T1", "Tank T1 overflowing", AlarmPriority.High, from, to, Started.AddSeconds(seconds), sim.AddMinutes(seconds));

        journal.Append(Entry("j1", AlarmState.Normal, AlarmState.Unacknowledged, 0));
        journal.Append(Entry("j2", AlarmState.Normal, AlarmState.Unacknowledged, 1));
        journal.Append(Entry("j1", AlarmState.Unacknowledged, AlarmState.Acknowledged, 2));
        journal.Append(Entry(null, AlarmState.Acknowledged, AlarmState.Normal, 3) with { SimTime = null });
        await journal.Writer.FlushAsync(ct);

        var j1 = await journal.ReadAsync(new RunId("j1"), 100, ct);
        Assert.Equal([(AlarmState.Normal, AlarmState.Unacknowledged), (AlarmState.Unacknowledged, AlarmState.Acknowledged)], j1.Select(e => (e.From, e.To)));
        Assert.Equal(Entry("j1", AlarmState.Unacknowledged, AlarmState.Acknowledged, 2) with { Id = j1[1].Id }, j1[1]);
        Assert.True(j1[0].Id < j1[1].Id);

        var newest = await journal.ReadAsync(null, 2, ct);
        Assert.Equal([2, 3], newest.Select(e => (int)(e.WallTime - Started).TotalSeconds));
        Assert.Null(newest[1].SimTime);
    }
}
