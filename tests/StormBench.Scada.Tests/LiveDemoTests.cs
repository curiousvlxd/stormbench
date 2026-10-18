using StormBench.Application.Scenarios;
using StormBench.Application.Site;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using StormBench.Scada.Hmi.Live;

namespace StormBench.Scada.Tests;

/// <summary>The guided demo: faults land at their steps, and the demo cleans up after itself.</summary>
public class LiveDemoTests
{
    private static ScenarioStatus Status(ScenarioState state, long run, long step) =>
        new(Presence.Online, state, run, LiveDemo.Storm, LiveDemo.Algorithm, LiveDemo.PaceSeconds, step, 431,
            null, null, 0, null, false, null, null, null);

    private static string Verdict() => "7.5% less into the river";

    private static readonly GateId V2 = new("V2");

    private static GateId? Moved() => new GateId("V4");

    [Fact]
    public void Nothing_happens_before_the_simulator_takes_up_the_storm()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);

        Assert.Empty(demo.Advance(Status(ScenarioState.Finished, 3, 431), Verdict, Moved));
        Assert.False(demo.Started);
        Assert.Null(demo.Chapter(gateCaught: false));
    }

    [Fact]
    public void Faults_land_at_their_steps_and_are_cleared_again()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);

        Assert.Equal([DemoCommand.Overview], demo.Advance(Status(ScenarioState.Running, 4, 0), Verdict, Moved));
        Assert.Equal(1, demo.Chapter(false)!.Number);
        Assert.Empty(demo.Advance(Status(ScenarioState.Running, 4, 40), Verdict, Moved));
        Assert.Equal([DemoCommand.CutLink, DemoCommand.FocusCenter], demo.Advance(Status(ScenarioState.Running, 4, 86), Verdict, Moved));
        Assert.Equal("The control room goes dark", demo.Chapter(false)!.Title);

        Assert.Equal(
            [DemoCommand.FocusPlc, DemoCommand.RestoreLink, DemoCommand.Overview, DemoCommand.JamGate, DemoCommand.FocusGate],
            demo.Advance(Status(ScenarioState.Running, 4, 255), Verdict, Moved));
        Assert.Equal("Will SCADA notice?", demo.Chapter(false)!.Subtitle);
        Assert.Equal("SCADA caught it", demo.Chapter(true)!.Subtitle);
        Assert.Equal(new GateId("V4"), demo.Gate);
        Assert.Equal([DemoCommand.FreeGate, DemoCommand.Overview], demo.Advance(Status(ScenarioState.Running, 4, 350), Verdict, Moved));
    }

    [Fact]
    public void The_jam_waits_for_a_gate_the_controller_has_opened()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);
        demo.Advance(Status(ScenarioState.Running, 4, 200), Verdict, Moved);

        Assert.Empty(demo.Advance(Status(ScenarioState.Running, 4, 260), Verdict, () => null));
        Assert.Equal([DemoCommand.JamGate, DemoCommand.FocusGate], demo.Advance(Status(ScenarioState.Running, 4, 270), Verdict, () => new GateId("V6")));
        Assert.Equal(new GateId("V6"), demo.Gate);
    }

    [Fact]
    public void Without_a_moved_gate_the_jam_falls_back_to_the_first_station()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);
        demo.Advance(Status(ScenarioState.Running, 4, 200), Verdict, () => null);

        Assert.Equal([DemoCommand.JamGate, DemoCommand.FocusGate], demo.Advance(Status(ScenarioState.Running, 4, 330), Verdict, () => null));
        Assert.Equal(V2, demo.Gate);
    }

    [Fact]
    public void A_finished_storm_ends_with_the_verdict()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);
        demo.Advance(Status(ScenarioState.Running, 4, 10), Verdict, Moved);

        Assert.Equal([DemoCommand.Overview], demo.Advance(Status(ScenarioState.Finished, 4, 431), Verdict, Moved));

        Assert.True(demo.Done);
        Assert.Equal(new DemoChapter(LiveDemo.Chapters, LiveDemo.Chapters, "The verdict", "7.5% less into the river"), demo.Chapter(false));
    }

    [Fact]
    public void A_stopped_storm_clears_the_faults_the_demo_set()
    {
        var demo = new LiveDemo(runBefore: 3, gate: V2);
        demo.Advance(Status(ScenarioState.Running, 4, 90), Verdict, Moved);

        Assert.Equal([DemoCommand.RestoreLink, DemoCommand.FreeGate], demo.Advance(Status(ScenarioState.Stopped, 4, 120), Verdict, Moved));
        Assert.True(demo.Done);
        Assert.Null(demo.Chapter(false));
        Assert.Empty(demo.Abort());
    }
}
