using System.Globalization;
using System.Text.RegularExpressions;
using StormBench.Domain.Control;

namespace StormBench.Domain.Tests;

/// <summary>Same cases as simulator/tests/test_control.py: the three implementations must agree.</summary>
public partial class EfdCoordinatorTests
{
    private static readonly double[] Base = [0.2366, 0.6508, 0.3523, 0.4303];
    private static readonly double[] Low = [0.1075, 0.3159, 0.1894, 0.1687];

    private static double[] Propose(EfdVariant variant, params double[] depth) =>
        EfdCoordinator.Propose(depth, new EfdParameters { Variant = variant });

    [Fact]
    public void Published_opens_only_the_strictly_fullest_tank() =>
        Assert.Equal([Base[0], Base[1], Base[2], 1.0], Propose(EfdVariant.Published, 2.0, 1.5, 0.3, 4.9));

    [Fact]
    public void Published_ignores_the_low_setting_and_the_threshold() =>
        Assert.Equal([Base[0], 1.0, Base[2], Base[3]], Propose(EfdVariant.Published, 0.5, 0.9, 0.2, 0.1));

    [Fact]
    public void Intended_throttles_the_emptiest_tank() =>
        Assert.Equal([Base[0], Base[1], Low[2], 1.0], Propose(EfdVariant.Intended, 2.0, 1.5, 0.3, 4.9));

    [Fact]
    public void Intended_keeps_base_case_below_threshold() =>
        Assert.Equal(Base, Propose(EfdVariant.Intended, 0.5, 0.9, 0.2, 0.1));

    [Theory]
    [InlineData(EfdVariant.Published)]
    [InlineData(EfdVariant.Intended)]
    public void Ties_keep_base_setting(EfdVariant variant) =>
        Assert.Equal(Base, Propose(variant, 2.0, 2.0, 1.0, 1.0));

    [Fact]
    public void Nan_level_is_never_fullest() =>
        Assert.Equal([Base[0], Base[1], Base[2], Base[3]], Propose(EfdVariant.Published, double.NaN, 1.5, 1.5, 0.3));

    [Fact]
    public void Default_parameters_match_the_plc_and_the_python_network_file()
    {
        var root = FindRoot();
        var st = File.ReadAllText(Path.Combine(root, "plc", "src", "main.st"));
        var toml = File.ReadAllText(Path.Combine(root, "simulator", "stormbench_sim", "networks", "astlingen.toml"));
        var defaults = new EfdParameters();

        Assert.Equal(defaults.BaseSetting, ArrayConstant(st, "BASE_SETTING"));
        Assert.Equal(defaults.LowSetting, ArrayConstant(st, "LOW_SETTING"));
        Assert.Equal(defaults.BaseSetting, TomlValues(toml, "base_setting"));
        Assert.Equal(defaults.LowSetting, TomlValues(toml, "low_setting"));
        Assert.Contains($"efd_threshold_m = {defaults.ThresholdDepth.ToString("0.0", CultureInfo.InvariantCulture)}", toml);
    }

    private static double[] ArrayConstant(string st, string name)
    {
        var match = Regex.Match(st, name + @" : ARRAY\[1\.\.4\] OF LREAL := \[([^\]]+)\];");
        Assert.True(match.Success, name);
        return match.Groups[1].Value.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
    }

    private static double[] TomlValues(string toml, string key) =>
        Regex.Matches(toml, key + @" = ([0-9.]+)")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

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
