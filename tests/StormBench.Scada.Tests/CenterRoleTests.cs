using StormBench.Domain.Control;
using StormBench.Domain.Scenarios;
using StormBench.Infrastructure.PrimaryHost;
using WireCatalog = StormBench.Contracts.ScenarioCatalog;

namespace StormBench.Scada.Tests;

/// <summary>Who proposes in mode 3, read from the catalog Sim01 publishes: Scada's EFD, unless the console runs an algorithm of another center (ADR 0017).</summary>
public class CenterRoleTests
{
    private static readonly ScenarioCatalog Catalog = WireCatalog.Parse("""
        {"storms": [{"id": "2000-05-30", "label": "30 May 2000", "description": "d",
                     "start": "2000-05-30T00:00:00", "end": "2000-05-31T12:00:00", "steps": 431}],
         "algorithms": [{"id": "efd-plc", "label": "EFD in the PLC", "description": "d", "mode": 1, "center": null},
                        {"id": "efd-center", "label": "EFD from the center", "description": "d", "mode": 3, "center": "scada"},
                        {"id": "mpc-center", "label": "MPC, simulator as center", "description": "d", "mode": 3, "center": "simulator"}],
         "paces": [0.1], "defaultStorm": "2000-05-30", "defaultAlgorithm": "efd-center", "defaultPace": 0.1}
        """)!.ToDomain();

    [Theory]
    [InlineData("running", "mpc-center", true)]
    [InlineData("preparing", "mpc-center", true)]
    [InlineData("running", "efd-center", false)]
    [InlineData("running", "efd-plc", false)]
    [InlineData("finished", "mpc-center", false)]
    [InlineData("idle", null, false)]
    [InlineData(null, null, false)]
    public void Scada_stands_aside_only_while_another_center_runs_the_storm(string? state, string? algorithm, bool expected) =>
        Assert.Equal(
            expected,
            CenterRole.AnotherCenterProposes(ScenarioStateWire.Parse(state), algorithm is null ? null : new AlgorithmId(algorithm), Catalog));

    [Fact]
    public void Without_a_catalog_Scada_is_the_center() =>
        Assert.False(CenterRole.AnotherCenterProposes(ScenarioState.Running, new AlgorithmId("mpc-center"), null));

    [Fact]
    public void Catalog_without_center_field_still_parses() =>
        Assert.Null(WireCatalog.Parse("""
            {"storms": [{"id": "s", "label": "l", "description": "d", "start": "2000-05-30T00:00:00", "end": "2000-05-31T00:00:00", "steps": 1}],
             "algorithms": [{"id": "efd-center", "label": "l", "description": "d", "mode": 3}],
             "paces": [0.1], "defaultStorm": "s", "defaultAlgorithm": "efd-center", "defaultPace": 0.1}
            """)!.ToDomain().Algorithm(new AlgorithmId("efd-center"))!.Center);

    [Fact]
    public void Mode_3_is_labelled_by_the_center_of_the_running_algorithm()
    {
        Assert.Equal("MPC, simulator as center", Hmi.Fmt.Mode(ControlMode.EfdCenter, Catalog.Algorithm(new AlgorithmId("mpc-center"))));
        Assert.Equal("EFD from the center", Hmi.Fmt.Mode(ControlMode.EfdCenter, Catalog.Algorithm(new AlgorithmId("efd-center"))));
        Assert.Equal("Base Case (local)", Hmi.Fmt.Mode(ControlMode.Local, Catalog.Algorithm(new AlgorithmId("mpc-center"))));
    }
}
