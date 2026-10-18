using Microsoft.Extensions.Time.Testing;
using StormBench.Application.Alarms;
using StormBench.Domain.Alarms;

namespace StormBench.Scada.Tests;

/// <summary>ISA-18.2 alarm states: nothing leaves the list before it is both cleared and acknowledged.</summary>
public class AlarmEngineTests
{
    private static readonly AlarmDefinition High = new("T1.Overflow", "T1", "Tank T1 overflowing", AlarmPriority.High);
    private static readonly AlarmDefinition Low = new("PLC01.Clamped", "PLC01", "Setpoint limited", AlarmPriority.Low);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly AlarmEngine _engine;

    public AlarmEngineTests() => _engine = new AlarmEngine(_clock);

    private AlarmState? State(string id) => _engine.Current().FirstOrDefault(a => a.Definition.Id == id)?.State;

    [Fact]
    public void Condition_raises_an_unacknowledged_alarm_with_its_times()
    {
        var simTime = new DateTime(2000, 5, 30, 22, 25, 0);
        _engine.Apply([(High, true)], simTime);

        var alarm = Assert.Single(_engine.Current());
        Assert.Equal(AlarmState.Unacknowledged, alarm.State);
        Assert.Equal(_clock.GetUtcNow(), alarm.ActivatedAt);
        Assert.Equal(simTime, alarm.SimTime);
        Assert.True(alarm.Active);
        Assert.True(alarm.NeedsAcknowledge);
    }

    [Fact]
    public void Acknowledged_alarm_leaves_when_the_condition_clears()
    {
        _engine.Apply([(High, true)]);
        Assert.True(_engine.Acknowledge(High.Id));
        Assert.Equal(AlarmState.Acknowledged, State(High.Id));

        _engine.Apply([(High, false)]);

        Assert.Empty(_engine.Current());
    }

    [Fact]
    public void Alarm_that_clears_first_waits_for_acknowledgement()
    {
        _engine.Apply([(High, true)]);
        _clock.Advance(TimeSpan.FromMinutes(1));
        _engine.Apply([(High, false)]);

        var alarm = Assert.Single(_engine.Current());
        Assert.Equal(AlarmState.ReturnedUnacknowledged, alarm.State);
        Assert.Equal(_clock.GetUtcNow(), alarm.ReturnedAt);
        Assert.False(alarm.Active);

        Assert.True(_engine.Acknowledge(High.Id));
        Assert.Empty(_engine.Current());
    }

    [Fact]
    public void Returned_alarm_that_comes_back_is_a_new_activation()
    {
        _engine.Apply([(High, true)]);
        _engine.Apply([(High, false)]);
        _clock.Advance(TimeSpan.FromMinutes(5));

        _engine.Apply([(High, true)]);

        var alarm = Assert.Single(_engine.Current());
        Assert.Equal(AlarmState.Unacknowledged, alarm.State);
        Assert.Equal(_clock.GetUtcNow(), alarm.ActivatedAt);
    }

    [Fact]
    public void Conditions_without_good_data_keep_their_state()
    {
        _engine.Apply([(High, true)]);
        var version = _engine.Version;

        _engine.Apply([]);

        Assert.Equal(AlarmState.Unacknowledged, State(High.Id));
        Assert.Equal(version, _engine.Version);
    }

    [Fact]
    public void Acknowledging_twice_or_an_unknown_alarm_changes_nothing()
    {
        _engine.Apply([(High, true)]);
        Assert.True(_engine.Acknowledge(High.Id));

        Assert.False(_engine.Acknowledge(High.Id));
        Assert.False(_engine.Acknowledge("nothing"));
    }

    [Fact]
    public void Acknowledge_all_counts_what_it_acknowledged()
    {
        _engine.Apply([(High, true), (Low, true)]);
        _engine.Acknowledge(Low.Id);

        Assert.Equal(1, _engine.AcknowledgeAll());
        Assert.All(_engine.Current(), a => Assert.Equal(AlarmState.Acknowledged, a.State));
    }

    [Fact]
    public void List_is_ordered_by_priority_then_newest_first()
    {
        var medium1 = new AlarmDefinition("a", "PLC01", "first", AlarmPriority.Medium);
        var medium2 = new AlarmDefinition("b", "PLC01", "second", AlarmPriority.Medium);
        _engine.Apply([(Low, true), (medium1, true)]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _engine.Apply([(medium2, true), (High, true)]);

        Assert.Equal(["T1.Overflow", "b", "a", "PLC01.Clamped"], _engine.Current().Select(a => a.Definition.Id));
    }
}
