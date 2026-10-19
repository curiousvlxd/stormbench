using System.Globalization;
using System.Reflection;

namespace StormBench.Infrastructure.Postgres;

/// <summary>One step of the schema: <c>Migrations/NNNN_name.sql</c>, applied once, in order of its number.</summary>
public sealed record SchemaMigration(int Version, string Name, string Sql);

/// <summary>The schema's migrations, embedded in the assembly from <c>Postgres/Migrations</c>.</summary>
public static class SchemaMigrations
{
    private const string Prefix = "migrations/";

    public static IReadOnlyList<SchemaMigration> All { get; } = Load();

    private static List<SchemaMigration> Load()
    {
        var assembly = typeof(SchemaMigrations).Assembly;
        return [.. assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => Read(assembly, n))
            .OrderBy(m => m.Version)];
    }

    private static SchemaMigration Read(Assembly assembly, string resource)
    {
        var file = Path.GetFileNameWithoutExtension(resource[Prefix.Length..]);
        var cut = file.IndexOf('_', StringComparison.Ordinal);
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return new SchemaMigration(int.Parse(file[..cut], CultureInfo.InvariantCulture), file[(cut + 1)..], reader.ReadToEnd());
    }
}
