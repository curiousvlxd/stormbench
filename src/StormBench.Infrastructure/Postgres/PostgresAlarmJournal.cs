using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using StormBench.Application.Alarms;
using StormBench.Domain.Alarms;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>
/// The alarm journal in the table <c>alarm_journal</c>, append only, written in the background: a
/// batch is one statement over arrays of its columns. Without a store nothing is queued.
/// </summary>
public sealed class PostgresAlarmJournal : IAlarmJournalStore
{
    private const string Insert = """
        INSERT INTO alarm_journal (run_id, alarm_id, source, message, priority, from_state, to_state, wall_time, sim_time)
        SELECT * FROM unnest(
            @RunId::text[], @AlarmId::text[], @Source::text[], @Message::text[], @Priority::smallint[],
            @FromState::text[], @ToState::text[], @WallTime::timestamptz[], @SimTime::timestamp[])
        """;

    private const string Columns = """
        id AS Id, run_id AS RunId, alarm_id AS AlarmId, source AS Source, message AS Message, priority AS Priority,
        from_state AS FromState, to_state AS ToState, wall_time AS WallTime, sim_time AS SimTime
        """;

    private const string SelectRun = $"""
        SELECT * FROM (SELECT {Columns} FROM alarm_journal WHERE run_id = @Run ORDER BY id DESC LIMIT @Limit) newest
        ORDER BY Id
        """;

    private const string SelectAll = $"""
        SELECT * FROM (SELECT {Columns} FROM alarm_journal ORDER BY id DESC LIMIT @Limit) newest
        ORDER BY Id
        """;

    private readonly ScadaDatabase _database;

    public PostgresAlarmJournal(ScadaDatabase database, ILogger<PostgresAlarmJournal> logger)
    {
        _database = database;
        Writer = new BatchWriter<JournalEntry>("Alarm journal", WriteAsync, logger, capacity: 50_000, maxBatch: 1_000);
    }

    /// <summary>Hosted service that writes the queued entries.</summary>
    public BatchWriter<JournalEntry> Writer { get; }

    public void Append(JournalEntry entry)
    {
        if (_database.Enabled)
        {
            Writer.Enqueue(entry);
        }
    }

    public async Task<IReadOnlyList<JournalEntry>> ReadAsync(RunId? run, int limit, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _database.OpenAsync(cancellationToken);
            var query = new CommandDefinition(run is null ? SelectAll : SelectRun, new { Run = run?.Value, Limit = (long)limit }, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<EntryRow>(query);
            return [.. rows.Select(r => r.ToEntry())];
        }
        catch (NpgsqlException ex)
        {
            throw Sql.Unreachable(ex);
        }
    }

    private async Task WriteAsync(IReadOnlyList<JournalEntry> entries, CancellationToken cancellationToken)
    {
        var columns = new
        {
            RunId = entries.Select(e => e.Run?.Value).ToArray(),
            AlarmId = entries.Select(e => e.AlarmId).ToArray(),
            Source = entries.Select(e => e.Source).ToArray(),
            Message = entries.Select(e => e.Message).ToArray(),
            Priority = entries.Select(e => (short)e.Priority).ToArray(),
            FromState = entries.Select(e => e.From.ToString()).ToArray(),
            ToState = entries.Select(e => e.To.ToString()).ToArray(),
            WallTime = entries.Select(e => Sql.WallTime(e.WallTime)).ToArray(),
            SimTime = entries.Select(e => Sql.ModelTime(e.SimTime)).ToArray(),
        };
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Insert, columns, cancellationToken: cancellationToken));
    }

    /// <summary>A row of <c>alarm_journal</c> as PostgreSQL returns it.</summary>
    private sealed class EntryRow
    {
        public long Id { get; init; }
        public string? RunId { get; init; }
        public string AlarmId { get; init; } = "";
        public string Source { get; init; } = "";
        public string Message { get; init; } = "";
        public short Priority { get; init; }
        public string FromState { get; init; } = "";
        public string ToState { get; init; } = "";
        public DateTime WallTime { get; init; }
        public DateTime? SimTime { get; init; }

        public JournalEntry ToEntry() => new(
            Id, RunId is { } run ? new RunId(run) : null, AlarmId, Source, Message, (AlarmPriority)Priority,
            Enum.Parse<AlarmState>(FromState), Enum.Parse<AlarmState>(ToState), Sql.FromWallTime(WallTime), SimTime);
    }
}
