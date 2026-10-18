using Microsoft.Extensions.Logging;
using Npgsql;
using StormBench.Application.Alarms;
using StormBench.Domain.Alarms;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>The alarm journal in the table <c>alarm_journal</c>, append only, written in the background.</summary>
public sealed class PostgresAlarmJournal : IAlarmJournalStore
{
    private const string Insert = """
        INSERT INTO alarm_journal (run_id, alarm_id, source, message, priority, from_state, to_state, wall_time, sim_time)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
        """;

    private const string SelectRun = """
        SELECT * FROM (
            SELECT id, run_id, alarm_id, source, message, priority, from_state, to_state, wall_time, sim_time
            FROM alarm_journal WHERE run_id = $1 ORDER BY id DESC LIMIT $2) newest
        ORDER BY id
        """;

    private const string SelectAll = """
        SELECT * FROM (
            SELECT id, run_id, alarm_id, source, message, priority, from_state, to_state, wall_time, sim_time
            FROM alarm_journal ORDER BY id DESC LIMIT $1) newest
        ORDER BY id
        """;

    private readonly ScadaDatabase _database;

    public PostgresAlarmJournal(ScadaDatabase database, ILogger<PostgresAlarmJournal> logger)
    {
        _database = database;
        Writer = new BatchWriter<JournalEntry>("Alarm journal", WriteAsync, logger, capacity: 50_000, maxBatch: 1_000);
    }

    /// <summary>Hosted service that writes the queued entries.</summary>
    public BatchWriter<JournalEntry> Writer { get; }

    public void Append(JournalEntry entry) => Writer.Enqueue(entry);

    public async Task<IReadOnlyList<JournalEntry>> ReadAsync(RunId? run, int limit, CancellationToken cancellationToken)
    {
        try
        {
            await _database.EnsureSchemaAsync(cancellationToken);
            await using var command = _database.DataSource.CreateCommand(run is null ? SelectAll : SelectRun);
            if (run is { } id)
            {
                command.Parameters.Add(Sql.Text(id.Value));
            }
            command.Parameters.Add(Sql.Int64(limit));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            List<JournalEntry> entries = [];
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new JournalEntry(
                    reader.GetInt64(0),
                    Sql.TextOrNull(reader, 1) is { } runId ? new RunId(runId) : null,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    (AlarmPriority)reader.GetInt16(5),
                    Enum.Parse<AlarmState>(reader.GetString(6)),
                    Enum.Parse<AlarmState>(reader.GetString(7)),
                    reader.GetFieldValue<DateTimeOffset>(8),
                    Sql.ValueOrNull<DateTime>(reader, 9)));
            }
            return entries;
        }
        catch (NpgsqlException ex)
        {
            throw Sql.Unreachable(ex);
        }
    }

    private async Task WriteAsync(IReadOnlyList<JournalEntry> entries, CancellationToken cancellationToken)
    {
        await _database.EnsureSchemaAsync(cancellationToken);
        await using var batch = _database.DataSource.CreateBatch();
        foreach (var e in entries)
        {
            var command = new NpgsqlBatchCommand(Insert);
            command.Parameters.Add(Sql.Text(e.Run?.Value));
            command.Parameters.Add(Sql.Text(e.AlarmId));
            command.Parameters.Add(Sql.Text(e.Source));
            command.Parameters.Add(Sql.Text(e.Message));
            command.Parameters.Add(Sql.Int16((short)e.Priority));
            command.Parameters.Add(Sql.Text(e.From.ToString()));
            command.Parameters.Add(Sql.Text(e.To.ToString()));
            command.Parameters.Add(Sql.WallTime(e.WallTime));
            command.Parameters.Add(Sql.ModelTime(e.SimTime));
            batch.BatchCommands.Add(command);
        }
        await batch.ExecuteNonQueryAsync(cancellationToken);
    }
}
