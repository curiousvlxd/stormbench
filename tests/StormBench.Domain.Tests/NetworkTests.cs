using StormBench.Domain.Network;

namespace StormBench.Domain.Tests;

/// <summary>The drainage network and its quantities: volumes, fill fractions and gate openings.</summary>
public class NetworkTests
{
    private static readonly DrainageNetwork Network = new(
        "mini",
        TimeSpan.FromMinutes(5),
        5.0,
        "Out",
        [new TankId("T1"), new TankId("T2")],
        [new OverflowPoint(new OverflowPointId("T1"), new TankId("T1")), new OverflowPoint(new OverflowPointId("CSO1"), null)],
        [new Station(new GateId("V2"), new TankId("T2"), new GateOpening(0.3), new GateOpening(0.1))],
        [new NetworkNode("T2", NodeKind.Storage, 2000, 3000), new NetworkNode("J1", NodeKind.Junction, 3500, 3000)],
        [new NetworkLink("V2", LinkKind.Orifice, "T2", "J1")]);

    [Fact]
    public void Stations_are_found_by_gate_and_by_tank()
    {
        Assert.True(Network.HasGate(new GateId("V2")));
        Assert.False(Network.HasGate(new GateId("V1")));
        Assert.True(Network.HasTank(new TankId("T1")));
        Assert.False(Network.HasTank(new TankId("T9")));
        Assert.Equal(new TankId("T2"), Network.StationOf(new GateId("V2"))?.Tank);
        Assert.Equal(new GateId("V2"), Network.StationAt(new TankId("T2"))?.Gate);
        Assert.Null(Network.StationAt(new TankId("T1")));
        Assert.Equal([new GateId("V2")], Network.Gates);
    }

    [Fact]
    public void Overflow_points_without_a_tank_are_weirs()
    {
        var weir = Assert.Single(Network.Weirs);

        Assert.Equal(new OverflowPointId("CSO1"), weir.Id);
        Assert.False(weir.IsTank);
        Assert.True(Network.OverflowPoints[0].IsTank);
    }

    [Theory]
    [InlineData(2.5, 0.5)]
    [InlineData(-0.1, 0.0)]
    [InlineData(6.0, 1.0)]
    public void Fill_is_the_level_over_the_tank_depth_clamped_to_empty_and_full(double level, double fill) =>
        Assert.Equal(fill, Network.Fill(level).Value);

    [Theory]
    [InlineData(2.5, true)]
    [InlineData(0.05, false)]
    [InlineData(4.95, false)]
    public void A_level_away_from_empty_and_full_is_between_them(double level, bool between) =>
        Assert.Equal(between, Network.IsBetweenEmptyAndFull(level, 0.05));

    [Fact]
    public void Tanks_need_a_depth() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrainageNetwork("x", TimeSpan.Zero, 0.0, "Out", [], [], [], [], []));

    [Fact]
    public void Volumes_add_subtract_and_compare_in_cubic_metres()
    {
        var spilled = new Volume(900);
        var reference = new Volume(1_000);

        Assert.Equal(new Volume(1_900), spilled + reference);
        Assert.Equal(new Volume(-100), spilled - reference);
        Assert.Equal(new Volume(-900), -spilled);
        Assert.True(spilled < reference && reference >= spilled && !(spilled > reference) && spilled <= new Volume(900));
        Assert.Equal(-0.1, spilled.RelativeTo(reference), 12);
        Assert.Equal(-1, spilled.CompareTo(reference));
        Assert.Equal(Volume.Zero, new Volume(0));
    }

    [Fact]
    public void A_fill_needs_a_positive_depth() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FillFraction.Of(1.0, 0.0));

    [Theory]
    [InlineData(1.0, 0.96, false)]
    [InlineData(1.0, 0.94, true)]
    [InlineData(0.24, 1.0, true)]
    public void A_gate_differs_from_its_setpoint_beyond_the_tolerance(double setpoint, double opening, bool differs) =>
        Assert.Equal(differs, new GateOpening(setpoint).DiffersFrom(new GateOpening(opening), 0.05));

    [Fact]
    public void Ids_are_their_text()
    {
        Assert.Equal("T2", new TankId("T2").ToString());
        Assert.Equal(new OverflowPointId("T2"), OverflowPointId.Of(new TankId("T2")));
        Assert.True(GateId.TryParse("V2", null, out var gate));
        Assert.Equal(new GateId("V2"), gate);
        Assert.False(TankId.TryParse(null, null, out _));
        Assert.Equal(new OverflowPointId("CSO7"), OverflowPointId.Parse("CSO7", null));
    }
}
