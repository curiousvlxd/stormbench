using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Contracts;
using StormBench.Domain.Kpi;
using StormBench.Domain.Network;
using StormBench.Scada.Hmi;
using StormBench.Scada.Hmi.Live;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>The Live view: the frame the 3D town draws and the narrator's plain-language lines.</summary>
public sealed class LiveViewTests
{
    private readonly SiteFixture _site = new();
    private readonly HmiFeed _feed;

    public LiveViewTests()
    {
        _feed = new HmiFeed(_site.Reader, new AlarmEngine(_site.Clock), new RunTrend(), _site);
    }

    private void Running(params SparkplugMetric[] extra)
    {
        _site.PlcBirth(
            SparkplugMetric.Double(PlcMetrics.Level("T1"), 2.5),
            SparkplugMetric.Double(PlcMetrics.Level("T2"), 1.0),
            SparkplugMetric.Double(PlcMetrics.Setpoint("V2"), 0.3),
            SparkplugMetric.Int64(PlcMetrics.CmdMode, (long)ControlMode.EfdCenter),
            SparkplugMetric.Int64(PlcMetrics.ModeActive, (long)ControlMode.EfdCenter),
            SparkplugMetric.Int64(PlcMetrics.Status, 0));
        _site.SimBirth([
            SparkplugMetric.String(SimMetrics.State, ScenarioStates.Running),
            SparkplugMetric.Int64(SimMetrics.Run, 1),
            SparkplugMetric.String(SimMetrics.Algorithm, "efd-center"),
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("T2"), true),
            SparkplugMetric.Double(SimMetrics.OverflowVolume("T2"), 120.0),
            SparkplugMetric.Double(SimMetrics.OverflowVolume("CSO1"), 0.0),
            SparkplugMetric.Double(SimMetrics.ValveOpening("V2"), 0.3),
            SparkplugMetric.Double(SimMetrics.RainIntensity, 6.0),
            .. extra]);
    }

    [Fact]
    public void Frame_carries_levels_gates_overflows_and_volumes()
    {
        Running();

        var frame = LiveFrame.From(_feed.Build());

        Assert.True(frame.Running);
        Assert.Equal(6.0, frame.Rain);
        Assert.Equal(0.5, frame.Tanks.Single(t => t.Id == "T1").Fill);
        Assert.True(frame.Tanks.Single(t => t.Id == "T2").Overflowing);
        var gate = Assert.Single(frame.Gates);
        Assert.Equal(("V2", 0.3, 0.3), (gate.Id, gate.Setpoint, gate.Opening));
        Assert.Equal("CSO1", Assert.Single(frame.Weirs).Id);
        Assert.Equal(120.0, frame.Volumes["T2"]);
        Assert.Equal("up", frame.Link);
        Assert.Equal("online", frame.Plc);
        Assert.False(frame.PlcLocal);
    }

    [Fact]
    public void A_cut_link_and_the_plcs_fallback_show_in_frame_and_story()
    {
        Running(SparkplugMetric.Boolean(SimMetrics.CenterLinkCut, true));
        _site.PlcData(SparkplugMetric.Int64(PlcMetrics.Status, 1));

        var view = _feed.Build();
        var frame = LiveFrame.From(view);
        var story = LiveStory.Tell(view);

        Assert.Equal("cut", frame.Link);
        Assert.True(frame.PlcLocal);
        Assert.Equal("Link cut: the PLC carries on alone", story[0].Text);
        Assert.Equal(Tone.Warning, story[0].Tone);
    }

    [Fact]
    public void Story_names_what_spills_in_plain_words()
    {
        Running();

        var story = LiveStory.Tell(_feed.Build());

        Assert.Equal(new StoryLine("Tank 2 spills into the river", Tone.Bad), Assert.Single(story));
    }

    [Fact]
    public void A_jammed_gate_is_told_before_and_after_the_scada_notices()
    {
        Running(SparkplugMetric.String(SimMetrics.StuckGate, "V2"));

        var story = LiveStory.Tell(_feed.Build());

        Assert.Equal("Gate 2 jammed: SCADA checking...", story[0].Text);
    }

    [Fact]
    public void A_sensor_frozen_on_an_empty_tank_is_told_as_a_blind_spot()
    {
        Running(SparkplugMetric.String(SimMetrics.FrozenSensor, "T1"));
        _site.PlcData(SparkplugMetric.Double(PlcMetrics.Level("T1"), 0.0));

        var story = LiveStory.Tell(_feed.Build());

        Assert.Equal("Tank 1 sensor frozen while empty: SCADA cannot tell", story[0].Text);
    }

    [Fact]
    public void Without_a_simulator_the_story_says_so()
    {
        var story = LiveStory.Tell(_feed.Build());

        Assert.Equal(Tone.Warning, Assert.Single(story).Tone);
    }

    [Theory]
    [InlineData("T4", "Tank 4")]
    [InlineData("CSO10", "Overflow 10")]
    [InlineData("V2", "Gate 2")]
    [InlineData("Out_to_WWTP", "Out_to_WWTP")]
    public void Model_names_become_plain_words(string id, string plain) => Assert.Equal(plain, LiveStory.Plain(id));

    [Theory]
    [InlineData(150.0, "about 1,000 bathtubs")]
    [InlineData(964.0, "about 6,400 bathtubs")]
    [InlineData(3000.0, "about 1.2 Olympic pools")]
    [InlineData(1.5, "about 10 bathtubs")]
    public void Volumes_become_things_people_can_picture(double m3, string picture) => Assert.Equal(picture, LiveStory.Picture(m3));

    [Fact]
    public void Verdict_compares_with_the_same_storm_without_smart_control()
    {
        var kpi = new OverflowKpi(new Volume(11_967), new Volume(12_931), new Volume(-964), -964.0 / 12_931, Comparison.Better, [], 10, new Volume(50_000));

        var verdict = LiveStory.Verdict(kpi, finished: true);

        Assert.Equal("Done: 7.5% less into the river", verdict.Text);
        Assert.Equal(Tone.Good, verdict.Tone);
    }
}
