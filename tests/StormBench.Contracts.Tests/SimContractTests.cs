using System.Reflection;
using System.Text.RegularExpressions;

namespace StormBench.Contracts.Tests;

/// <summary>The Sim01 contract exists twice, in C# for Scada and in Python for the simulator.</summary>
public partial class SimContractTests
{
    [GeneratedRegex("""^(?<name>[A-Z_]+) = "(?<value>[^"]*)"$""", RegexOptions.Multiline)]
    private static partial Regex PythonConstant();

    private static Dictionary<string, string> Python() =>
        PythonConstant().Matches(RepositoryFiles.Read("simulator", "stormbench_sim", "uns_contract.py"))
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

    private static string Snake(string pascal) => PascalBoundary().Replace(pascal, "$1_$2").ToUpperInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundary();

    [Fact]
    public void Every_metric_name_is_the_same_in_python()
    {
        var python = Python();
        var constants = typeof(SimMetrics).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.NotEmpty(constants);
        foreach (var (name, value) in constants)
        {
            var pythonName = Snake(name);
            Assert.True(python.TryGetValue(pythonName, out var pythonValue), $"{pythonName} missing in uns_contract.py");
            Assert.Equal(value, pythonValue);
        }
    }

    [Fact]
    public void Node_ids_and_per_point_names_match()
    {
        var python = Python();
        Assert.Equal(Uns.GroupId, python["GROUP_ID"]);
        Assert.Equal(Uns.SimNodeId, python["SIM_NODE_ID"]);
        Assert.Equal(Uns.PrimaryHostId, python["PRIMARY_HOST_ID"]);
        Assert.Equal(PlcMetrics.BdSeq, python["BD_SEQ"]);
        Assert.Equal(PlcMetrics.Rebirth, python["REBIRTH"]);

        var source = RepositoryFiles.Read("simulator", "stormbench_sim", "uns_contract.py");
        Assert.Contains("return f\"Overflow/{point}/Active\"", source, StringComparison.Ordinal);
        Assert.Contains("return f\"Overflow/{point}/Volume\"", source, StringComparison.Ordinal);
        Assert.Equal("Overflow/CSO7/Active", SimMetrics.OverflowActive("CSO7"));
        Assert.Equal("Overflow/CSO7/Volume", SimMetrics.OverflowVolume("CSO7"));
    }

    [Fact]
    public void Scenario_states_match()
    {
        var source = RepositoryFiles.Read("simulator", "stormbench_sim", "uns_contract.py");
        foreach (var state in new[]
                 {
                     ScenarioStates.Idle, ScenarioStates.Preparing, ScenarioStates.Running,
                     ScenarioStates.Finished, ScenarioStates.Stopped, ScenarioStates.Failed,
                 })
        {
            Assert.Contains($" = \"{state}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Network_description_reads_the_simulator_json()
    {
        var network = NetworkDescription.Parse("""
            {"name": "astlingen", "controlStepS": 300, "tankMaxDepthM": 5.0, "wwtp": "Out_to_WWTP",
             "tanks": ["T1"], "overflowPoints": ["T1", "CSO7"],
             "stations": [{"valve": "V2", "tank": "T2", "baseSetting": 0.2366, "lowSetting": 0.1075}],
             "nodes": [{"id": "T1", "kind": "storage", "x": 8591.8, "y": 4435.4}],
             "links": [{"id": "V1", "kind": "orifice", "from": "T1", "to": "J19"}],
             "labelOffsets": {"CSO10": [-330, 120]}}
            """);

        Assert.NotNull(network);
        Assert.Equal(300, network.ControlStepS);
        Assert.Equal(0.2366, network.Stations[0].BaseSetting);
        Assert.Equal(new NodeDescription("T1", "storage", 8591.8, 4435.4), network.Nodes[0]);
        Assert.Equal(new LinkDescription("V1", "orifice", "T1", "J19"), network.Links[0]);
        Assert.Equal([-330.0, 120.0], network.LabelOffsets!["CSO10"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("""{"name": "x", "tankMaxDepthM": 5.0}""")]
    [InlineData("""{"name": "x", "tankMaxDepthM": 0, "tanks": [], "overflowPoints": [], "stations": [], "nodes": [], "links": []}""")]
    public void Unusable_network_description_is_null(string? json)
    {
        Assert.Null(NetworkDescription.Parse(json));
    }

    [Fact]
    public void Catalog_reads_storms_in_model_time()
    {
        var catalog = ScenarioCatalog.Parse("""
            {"storms": [{"id": "2000-05-30", "label": "30 May 2000", "description": "Medium storm",
                         "start": "2000-05-30T00:00:00", "end": "2000-05-31T12:00:00", "steps": 431}],
             "algorithms": [{"id": "efd-center", "label": "EFD from the center", "description": "d", "mode": 3}],
             "paces": [0.5, 0.2, 0.1, 0.05, 0.0], "defaultStorm": "2000-05-30", "defaultAlgorithm": "efd-center", "defaultPace": 0.1}
            """);

        Assert.NotNull(catalog);
        var storm = catalog.Storm("2000-05-30")!;
        Assert.Equal(new DateTime(2000, 5, 30, 0, 0, 0), storm.Start);
        Assert.Equal(DateTimeKind.Unspecified, storm.Start.Kind);
        Assert.Equal(431, storm.Steps);
        Assert.Equal((long)ControlMode.EfdCenter, catalog.Algorithm("efd-center")!.Mode);
        Assert.Null(catalog.Storm("nope"));
        Assert.Equal([0.5, 0.2, 0.1, 0.05, 0.0], catalog.Paces);
    }

    [Fact]
    public void Empty_catalog_is_null()
    {
        Assert.Null(ScenarioCatalog.Parse("""{"storms": [], "algorithms": [], "paces": []}"""));
    }
}
