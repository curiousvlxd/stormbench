using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using StormBench.Application.Runs;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Infrastructure.Postgres;

/// <summary>
/// Run summaries in the table <c>runs</c>, one row per run, written in the background: a batch is
/// one statement over arrays of its columns. Without a store nothing is queued.
/// </summary>
public sealed class PostgresRunStore : IRunStore
{
    private const string Upsert = """
        INSERT INTO runs (id, sim_run, storm, storm_label, algorithm, algorithm_label, pace_s, state,
                          storm_start, storm_end, started_at, finished_at, steps, steps_planned,
                          overflow_m3, base_case_m3, change_pct, wwtp_inflow_m3, mismatches)
        SELECT * FROM unnest(
            @Id::text[], @SimRun::bigint[], @Storm::text[], @StormLabel::text[], @Algorithm::text[],
            @AlgorithmLabel::text[], @PaceS::double precision[], @State::text[], @StormStart::timestamp[],
            @StormEnd::timestamp[], @StartedAt::timestamptz[], @FinishedAt::timestamptz[], @Steps::bigint[],
            @StepsPlanned::bigint[], @Overflow::double precision[], @BaseCase::double precision[],
            @ChangePct::double precision[], @WwtpInflow::double precision[], @Mismatches::bigint[])
        ON CONFLICT (id) DO UPDATE SET
            state = EXCLUDED.state, finished_at = EXCLUDED.finished_at, steps = EXCLUDED.steps,
            steps_planned = EXCLUDED.steps_planned, overflow_m3 = EXCLUDED.overflow_m3,
            base_case_m3 = EXCLUDED.base_case_m3, change_pct = EXCLUDED.change_pct,
            wwtp_inflow_m3 = EXCLUDED.wwtp_inflow_m3, mismatches = EXCLUDED.mismatches
        """;

    private const string Select = """
        SELECT id AS Id, sim_run AS SimRun, storm AS Storm, storm_label AS StormLabel, algorithm AS Algorithm,
               algorithm_label AS AlgorithmLabel, pace_s AS PaceS, state AS State, storm_start AS StormStart,
               storm_end AS StormEnd, started_at AS StartedAt, finished_at AS FinishedAt, steps AS Steps,
               steps_planned AS StepsPlanned, overflow_m3 AS Overflow, base_case_m3 AS BaseCase,
               change_pct AS ChangePct, wwtp_inflow_m3 AS WwtpInflow, mismatches AS Mismatches
        FROM runs ORDER BY started_at DESC, id DESC LIMIT @Limit
        """;

    private readonly ScadaDatabase _database;

    public PostgresRunStore(ScadaDatabase database, ILogger<PostgresRunStore> logger)
    {
        _database = database;
        Writer = new BatchWriter<RunSummary>("Run store", WriteAsync, logger, capacity: 1_000, maxBatch: 100);
    }

    /// <summary>Hosted service that writes the queued summaries.</summary>
    public BatchWriter<RunSummary> Writer { get; }

    public void Save(RunSummary run)
    {
        if (_database.Enabled)
        {
            Writer.Enqueue(run);
        }
    }

    public async Task<IReadOnlyList<RunSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _database.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<RunRow>(new CommandDefinition(Select, new { Limit = (long)limit }, cancellationToken: cancellationToken));
            return [.. rows.Select(r => r.ToSummary())];
        }
        catch (NpgsqlException ex)
        {
            throw Sql.Unreachable(ex);
        }
    }

    /// <summary>One statement per batch; a run queued twice (started, then finished) is written once, as it was last.</summary>
    private async Task WriteAsync(IReadOnlyList<RunSummary> queued, CancellationToken cancellationToken)
    {
        var runs = queued.GroupBy(r => r.Id).Select(g => g.Last()).ToList();
        var columns = new
        {
            Id = runs.Select(r => r.Id.Value).ToArray(),
            SimRun = runs.Select(r => r.SimRun).ToArray(),
            Storm = runs.Select(r => r.Storm.Value).ToArray(),
            StormLabel = runs.Select(r => r.StormLabel).ToArray(),
            Algorithm = runs.Select(r => r.Algorithm.Value).ToArray(),
            AlgorithmLabel = runs.Select(r => r.AlgorithmLabel).ToArray(),
            PaceS = runs.Select(r => r.PaceSeconds).ToArray(),
            State = runs.Select(r => RunStateText.Of(r.State)).ToArray(),
            StormStart = runs.Select(r => Sql.ModelTime(r.StormStart)).ToArray(),
            StormEnd = runs.Select(r => Sql.ModelTime(r.StormEnd)).ToArray(),
            StartedAt = runs.Select(r => Sql.WallTime(r.StartedAt)).ToArray(),
            FinishedAt = runs.Select(r => Sql.WallTime(r.FinishedAt)).ToArray(),
            Steps = runs.Select(r => r.Steps).ToArray(),
            StepsPlanned = runs.Select(r => r.StepsPlanned).ToArray(),
            Overflow = runs.Select(r => r.Overflow.CubicMeters).ToArray(),
            BaseCase = runs.Select(r => r.BaseCase?.CubicMeters).ToArray(),
            ChangePct = runs.Select(r => r.ChangePercent).ToArray(),
            WwtpInflow = runs.Select(r => r.WwtpInflow?.CubicMeters).ToArray(),
            Mismatches = runs.Select(r => r.Mismatches).ToArray(),
        };
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Upsert, columns, cancellationToken: cancellationToken));
    }

    /// <summary>A row of <c>runs</c> as PostgreSQL returns it.</summary>
    private sealed class RunRow
    {
        public string Id { get; init; } = "";
        public long SimRun { get; init; }
        public string Storm { get; init; } = "";
        public string? StormLabel { get; init; }
        public string Algorithm { get; init; } = "";
        public string? AlgorithmLabel { get; init; }
        public double? PaceS { get; init; }
        public string State { get; init; } = "";
        public DateTime? StormStart { get; init; }
        public DateTime? StormEnd { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; init; }
        public long Steps { get; init; }
        public long StepsPlanned { get; init; }
        public double Overflow { get; init; }
        public double? BaseCase { get; init; }
        public double? ChangePct { get; init; }
        public double? WwtpInflow { get; init; }
        public long Mismatches { get; init; }

        public RunSummary ToSummary() => new(
            new RunId(Id), SimRun, new StormId(Storm), StormLabel, new AlgorithmId(Algorithm), AlgorithmLabel, PaceS,
            RunStateText.Parse(State), StormStart, StormEnd, Sql.FromWallTime(StartedAt), FinishedAt is { } f ? Sql.FromWallTime(f) : null,
            Steps, StepsPlanned, new Volume(Overflow), BaseCase is { } b ? new Volume(b) : null, ChangePct,
            WwtpInflow is { } w ? new Volume(w) : null, Mismatches);
    }
}
