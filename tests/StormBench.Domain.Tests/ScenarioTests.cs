using StormBench.Domain.Control;
using StormBench.Domain.Scenarios;

namespace StormBench.Domain.Tests;

/// <summary>The test console's catalog, the states of a storm and who proposes in mode 3.</summary>
public class ScenarioTests
{
    private static readonly ScenarioCatalog Catalog = new(
        [new StormWindow(new StormId("2000-05-30"), "30 May 2000", "d", new DateTime(2000, 5, 30), new DateTime(2000, 5, 31, 12, 0, 0), 431)],
        [
            new ControlAlgorithm(new AlgorithmId("efd-plc"), "EFD in the PLC", "d", ControlMode.EfdPublished, null),
            new ControlAlgorithm(new AlgorithmId("efd-center"), "EFD from the center", "d", ControlMode.EfdCenter, ControlCenter.Scada),
            new ControlAlgorithm(new AlgorithmId("mpc-center"), "MPC, simulator as center", "d", ControlMode.EfdCenter, ControlCenter.Simulator),
        ],
        [0.1],
        new StormId("2000-05-30"),
        new AlgorithmId("efd-center"),
        0.1);

    [Theory]
    [InlineData(ScenarioState.Idle, false, false)]
    [InlineData(ScenarioState.Preparing, true, false)]
    [InlineData(ScenarioState.Running, true, false)]
    [InlineData(ScenarioState.Finished, false, true)]
    [InlineData(ScenarioState.Stopped, false, true)]
    [InlineData(ScenarioState.Failed, false, true)]
    public void A_storm_is_busy_until_it_has_ended(ScenarioState state, bool busy, bool ended) =>
        Assert.Equal((busy, ended), (state.IsBusy, state.HasEnded));

    [Fact]
    public void The_catalog_finds_storms_and_algorithms_by_id()
    {
        Assert.Equal("30 May 2000", Catalog.Storm(new StormId("2000-05-30"))?.Label);
        Assert.Null(Catalog.Storm(new StormId("nope")));
        Assert.Null(Catalog.Storm(null));
        Assert.Equal(ControlCenter.Simulator, Catalog.Algorithm(new AlgorithmId("mpc-center"))?.Center);
        Assert.Null(Catalog.Algorithm(null));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(5.0, true)]
    [InlineData(-1.0, false)]
    [InlineData(5.1, false)]
    [InlineData(double.NaN, false)]
    public void The_pace_lies_between_as_fast_as_possible_and_five_seconds(double seconds, bool valid) =>
        Assert.Equal(valid, StormPace.IsValid(seconds));

    [Theory]
    [InlineData(ScenarioState.Running, "mpc-center", true)]
    [InlineData(ScenarioState.Preparing, "mpc-center", true)]
    [InlineData(ScenarioState.Running, "efd-center", false)]
    [InlineData(ScenarioState.Running, "efd-plc", false)]
    [InlineData(ScenarioState.Finished, "mpc-center", false)]
    [InlineData(ScenarioState.Running, "unknown", false)]
    public void Scada_stands_aside_only_while_another_center_runs_the_storm(ScenarioState state, string algorithm, bool expected) =>
        Assert.Equal(expected, CenterRole.AnotherCenterProposes(state, new AlgorithmId(algorithm), Catalog));

    [Fact]
    public void Without_a_catalog_or_an_algorithm_Scada_is_the_center()
    {
        Assert.False(CenterRole.AnotherCenterProposes(ScenarioState.Running, new AlgorithmId("mpc-center"), null));
        Assert.False(CenterRole.AnotherCenterProposes(ScenarioState.Running, null, Catalog));
    }
}
