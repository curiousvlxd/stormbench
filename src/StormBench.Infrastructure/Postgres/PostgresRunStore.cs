using Microsoft.Extensions.Logging;
using Npgsql;
using StormBench.Application.Runs;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Infrastructure.Postgres;

/// <summary>Run summaries in the table <c>runs</c>, one row per run, written in the background.</summary>
public sealed class PostgresRunStore : IRunStore
{
    private const string Upsert = """
        INSERT INTO runs (id, sim_run, storm, storm_label, algorithm, algorithm_label, pace_s, state,
                          storm_start, storm_end, started_at, finished_at, steps, steps_planned,
                          overflow_m3, base_case_m3, change_pct, wwtp_inflow_m3, mismatches)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19)
        ON CONFLICT (id) DO UPDATE SET
            state = EXCLUDED.state, finished_at = EXCLUDED.finished_at, steps = EXCLUDED.steps,
            steps_planned = EXCLUDED.steps_planned, overflow_m3 = EXCLUDED.overflow_m3,
            base_case_m3 = EXCLUDED.base_case_m3, change_pct = EXCLUDED.change_pct,
            wwtp_inflow_m3 = EXCLUDED.wwtp_inflow_m3, mismatches = EXCLUDED.mismatches
        """;

    private const string Select = """
        SELECT id, sim_run, storm, storm_label, algorithm, algorithm_label, pace_s, state,
               storm_start, storm_end, started_at, finished_at, steps, steps_planned,
               overflow_m3, base_case_m3, change_pct, wwtp_inflow_m3, mismatches
        FROM runs ORDER BY started_at DESC, id DESC LIMIT $1
        """;

    private readonly ScadaDatabase _database;

    public PostgresRunStore(ScadaDatabase database, ILogger<PostgresRunStore> logger)
    {
        _database = database;
        Writer = new BatchWriter<RunSummary>("Run store", WriteAsync, logger, capacity: 1_000, maxBatch: 100);
    }

    /// <summary>Hosted service that writes the queued summaries.</summary>
    public BatchWriter<RunSummary> Writer { get; }

    public void Save(RunSummary run) => Writer.Enqueue(run);

    public async Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        try
        {
            await _database.EnsureSchemaAsync(cancellationToken);
            await using var command = _database.DataSource.CreateCommand(Select);
            command.Parameters.Add(Sql.Int64(limit));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            List<RunSummary> runs = [];
            while (await reader.ReadAsync(cancellationToken))
            {
                runs.Add(new RunSummary(
                    new RunId(reader.GetString(0)),
                    reader.GetInt64(1),
                    new StormId(reader.GetString(2)),
                    Sql.TextOrNull(reader, 3),
                    new AlgorithmId(reader.GetString(4)),
                    Sql.TextOrNull(reader, 5),
                    Sql.ValueOrNull<double>(reader, 6),
                    RunStateText.Parse(reader.GetString(7)),
                    Sql.ValueOrNull<DateTime>(reader, 8),
                    Sql.ValueOrNull<DateTime>(reader, 9),
                    reader.GetFieldValue<DateTimeOffset>(10),
                    Sql.ValueOrNull<DateTimeOffset>(reader, 11),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    new Volume(reader.GetDouble(14)),
                    Sql.VolumeOrNull(reader, 15),
                    Sql.ValueOrNull<double>(reader, 16),
                    Sql.VolumeOrNull(reader, 17),
                    reader.GetInt64(18)));
            }
            return runs;
        }
        catch (NpgsqlException ex)
        {
            throw Sql.Unreachable(ex);
        }
    }

    private async Task WriteAsync(IReadOnlyList<RunSummary> runs, CancellationToken cancellationToken)
    {
        await _database.EnsureSchemaAsync(cancellationToken);
        await using var batch = _database.DataSource.CreateBatch();
        foreach (var run in runs)
        {
            var command = new NpgsqlBatchCommand(Upsert);
            command.Parameters.Add(Sql.Text(run.Id.Value));
            command.Parameters.Add(Sql.Int64(run.SimRun));
            command.Parameters.Add(Sql.Text(run.Storm.Value));
            command.Parameters.Add(Sql.Text(run.StormLabel));
            command.Parameters.Add(Sql.Text(run.Algorithm.Value));
            command.Parameters.Add(Sql.Text(run.AlgorithmLabel));
            command.Parameters.Add(Sql.Double(run.PaceSeconds));
            command.Parameters.Add(Sql.Text(RunStateText.Of(run.State)));
            command.Parameters.Add(Sql.ModelTime(run.StormStart));
            command.Parameters.Add(Sql.ModelTime(run.StormEnd));
            command.Parameters.Add(Sql.WallTime(run.StartedAt));
            command.Parameters.Add(Sql.WallTime(run.FinishedAt));
            command.Parameters.Add(Sql.Int64(run.Steps));
            command.Parameters.Add(Sql.Int64(run.StepsPlanned));
            command.Parameters.Add(Sql.Double(run.Overflow.CubicMeters));
            command.Parameters.Add(Sql.Double(run.BaseCase?.CubicMeters));
            command.Parameters.Add(Sql.Double(run.ChangePercent));
            command.Parameters.Add(Sql.Double(run.WwtpInflow?.CubicMeters));
            command.Parameters.Add(Sql.Int64(run.Mismatches));
            batch.BatchCommands.Add(command);
        }
        await batch.ExecuteNonQueryAsync(cancellationToken);
    }
}
