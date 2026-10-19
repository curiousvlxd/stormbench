using Npgsql;
using StormBench.Application.Runs;

namespace StormBench.Infrastructure.Postgres;

/// <summary>How the two kinds of time are kept, and what a failed read becomes.</summary>
internal static class Sql
{
    /// <summary>Model time has no time zone (ADR 0006): timestamp without time zone.</summary>
    public static DateTime? ModelTime(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Unspecified) : null;

    /// <summary>Wall time: timestamptz, written as UTC.</summary>
    public static DateTimeOffset? WallTime(DateTimeOffset? value) => value?.ToUniversalTime();

    /// <summary>A timestamptz as Npgsql reads it (UTC) back to wall time.</summary>
    public static DateTimeOffset FromWallTime(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    /// <summary>A read failed because PostgreSQL could not be reached or refused it.</summary>
    public static StoreUnavailableException Unreachable(NpgsqlException exception) =>
        new($"Run store not reachable: {exception.Message}", exception);
}
