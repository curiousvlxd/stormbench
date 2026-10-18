namespace StormBench.Contracts.Tests;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string Read(params string[] relative) => File.ReadAllText(Path.Combine([Root, .. relative]));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StormBench.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
