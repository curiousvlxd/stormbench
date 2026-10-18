using StormBench.Domain.Alarms;
using StormBench.Domain.Network;

namespace StormBench.Domain.Tests;

/// <summary>The ISA-18.2 lifecycle of one alarm and of the board that holds the site's alarms.</summary>
public class AlarmTests
{
    private static readonly AlarmDefinition High = new("T1.Overflow", "T1", "Tank T1 overflowing", AlarmPriority.High);
    private static readonly AlarmDefinition Low = new("PLC01.Clamped", "PLC01", "Setpoint limited", AlarmPriority.Low);
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Sim = new(2000, 5, 30, 22, 25, 0);

    [Theory]
    [InlineData(AlarmState.Normal, true, AlarmState.Unacknowledged)]
    [InlineData(AlarmState.Normal, false, AlarmState.Normal)]
    [InlineData(AlarmState.Unacknowledged, true, AlarmState.Unacknowledged)]
    [InlineData(AlarmState.Unacknowledged, false, AlarmState.ReturnedUnacknowledged)]
    [InlineData(AlarmState.Acknowledged, true, AlarmState.Acknowledged)]
    [InlineData(AlarmState.Acknowledged, false, AlarmState.Normal)]
    [InlineData(AlarmState.ReturnedUnacknowledged, true, AlarmState.Unacknowledged)]
    [InlineData(AlarmState.ReturnedUnacknowledged, false, AlarmState.ReturnedUnacknowledged)]
    public void A_condition_moves_the_alarm_through_its_lifecycle(AlarmState state, bool active, AlarmState next) =>
        Assert.Equal(next, Alarm.Next(state, active));

    [Theory]
    [InlineData(AlarmState.Unacknowledged, AlarmState.Acknowledged)]
    [InlineData(AlarmState.ReturnedUnacknowledged, AlarmState.Normal)]
    [InlineData(AlarmState.Acknowledged, AlarmState.Acknowledged)]
    [InlineData(AlarmState.Normal, AlarmState.Normal)]
    public void Only_an_unacknowledged_alarm_can_be_acknowledged(AlarmState state, AlarmState next) =>
        Assert.Equal(next, Alarm.AfterAcknowledge(state));

    [Fact]
    public void A_raised_alarm_is_active_and_waits_for_the_operator()
    {
        var alarm = Alarm.Raise(High, Now, Sim);

        Assert.Equal((AlarmState.Unacknowledged, Now, Sim), (alarm.State, alarm.ActivatedAt, alarm.SimTime));
        Assert.True(alarm.Active);
        Assert.True(alarm.NeedsAcknowledge);
        Assert.False((alarm with { State = AlarmState.ReturnedUnacknowledged }).Active);
        Assert.False((alarm with { State = AlarmState.Acknowledged }).NeedsAcknowledge);
    }

    [Fact]
    public void The_board_reports_every_change_and_drops_alarms_back_to_normal()
    {
        var board = new AlarmBoard();

        var raised = Assert.Single(board.Apply([(High, true), (Low, false)], Now, Sim));
        Assert.Equal((AlarmState.Normal, AlarmState.Unacknowledged, Sim), (raised.From, raised.To, raised.SimTime));
        Assert.Empty(board.Apply([(High, true)], Now, Sim));

        var acknowledged = board.Acknowledge(High.Id, Now.AddSeconds(5));
        Assert.Equal(AlarmState.Acknowledged, acknowledged?.To);
        Assert.Equal(Now.AddSeconds(5), Assert.Single(board.Current()).AcknowledgedAt);

        var cleared = Assert.Single(board.Apply([(High, false)], Now.AddSeconds(10), Sim.AddMinutes(5)));
        Assert.Equal(AlarmState.Normal, cleared.To);
        Assert.Empty(board.Current());
    }

    [Fact]
    public void A_condition_that_clears_first_stays_until_acknowledged()
    {
        var board = new AlarmBoard();
        board.Apply([(High, true)], Now, Sim);
        board.Apply([(High, false)], Now.AddSeconds(10), Sim.AddMinutes(5));

        var returned = Assert.Single(board.Current());
        Assert.Equal((AlarmState.ReturnedUnacknowledged, Now.AddSeconds(10)), (returned.State, returned.ReturnedAt));

        var transition = board.Acknowledge(High.Id, Now.AddSeconds(20));
        Assert.Equal((AlarmState.ReturnedUnacknowledged, AlarmState.Normal), (transition!.From, transition.To));
        Assert.Equal(Sim.AddMinutes(5), transition.SimTime);
        Assert.Empty(board.Current());
        Assert.Null(board.Acknowledge(High.Id, Now.AddSeconds(30)));
    }

    [Fact]
    public void Acknowledging_all_reaches_every_waiting_alarm_and_skips_the_acknowledged()
    {
        var board = new AlarmBoard();
        board.Apply([(High, true), (Low, true)], Now, Sim);
        board.Acknowledge(Low.Id, Now);

        var transitions = board.AcknowledgeAll(Now.AddSeconds(1));

        Assert.Equal([High.Id], transitions.Select(t => t.Definition.Id));
        Assert.All(board.Current(), a => Assert.Equal(AlarmState.Acknowledged, a.State));
    }

    [Fact]
    public void The_list_shows_the_highest_priority_first_then_the_newest()
    {
        var board = new AlarmBoard();
        var medium = AlarmDefinitions.GateDeviation(new GateId("V2"));
        board.Apply([(Low, true)], Now, Sim);
        board.Apply([(medium, true)], Now.AddSeconds(1), Sim);
        board.Apply([(High, true)], Now.AddSeconds(2), Sim);

        Assert.Equal([High.Id, medium.Id, Low.Id], board.Current().Select(a => a.Definition.Id));
    }

    [Fact]
    public void Definitions_name_tanks_structures_gates_and_frozen_readings()
    {
        Assert.Equal(
            ("T1.Overflow", "Tank T1 overflowing", AlarmPriority.High),
            Fields(AlarmDefinitions.Overflow(new OverflowPoint(new OverflowPointId("T1"), new TankId("T1")))));
        Assert.Equal(
            ("CSO7.Overflow", "CSO7 overflowing", AlarmPriority.High),
            Fields(AlarmDefinitions.Overflow(new OverflowPoint(new OverflowPointId("CSO7"), null))));
        Assert.Equal(("V2.GateDeviation", "Gate V2 does not follow its setpoint", AlarmPriority.Medium), Fields(AlarmDefinitions.GateDeviation(new GateId("V2"))));
        Assert.Equal(("T3.LevelFrozen", "Level reading of T3 frozen", AlarmPriority.Medium), Fields(AlarmDefinitions.LevelFrozen(new TankId("T3"))));
    }

    private static (string, string, AlarmPriority) Fields(AlarmDefinition d) => (d.Id, d.Message, d.Priority);
}
