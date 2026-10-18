using Npgsql;
using NpgsqlTypes;
using StormBench.Application.Runs;
using StormBench.Domain.Network;

namespace StormBench.Infrastructure.Postgres;

/// <summary>Parameters and nullable reads for the run store, with the column types spelled out.</summary>
internal static class Sql
{
    public static NpgsqlParameter Text(string? value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value };

    public static NpgsqlParameter Double(double? value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Double, Value = (object?)value ?? DBNull.Value };

    public static NpgsqlParameter Int64(long value) => new() { NpgsqlDbType = NpgsqlDbType.Bigint, Value = value };

    public static NpgsqlParameter Int16(short value) => new() { NpgsqlDbType = NpgsqlDbType.Smallint, Value = value };

    /// <summary>Model time has no time zone (ADR 0006): timestamp without time zone.</summary>
    public static NpgsqlParameter ModelTime(DateTime? value) => new()
    {
        NpgsqlDbType = NpgsqlDbType.Timestamp,
        Value = value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Unspecified) : DBNull.Value,
    };

    /// <summary>Wall time: timestamptz, written as UTC.</summary>
    public static NpgsqlParameter WallTime(DateTimeOffset? value) => new()
    {
        NpgsqlDbType = NpgsqlDbType.TimestampTz,
        Value = value is { } v ? v.ToUniversalTime() : DBNull.Value,
    };

    public static string? TextOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static T? ValueOrNull<T>(NpgsqlDataReader reader, int ordinal)
        where T : struct => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    public static Volume? VolumeOrNull(NpgsqlDataReader reader, int ordinal) =>
        ValueOrNull<double>(reader, ordinal) is { } m3 ? new Volume(m3) : null;

    /// <summary>A read failed because PostgreSQL could not be reached or refused it.</summary>
    public static StoreUnavailableException Unreachable(NpgsqlException exception) =>
        new($"Run store not reachable: {exception.Message}", exception);
}
