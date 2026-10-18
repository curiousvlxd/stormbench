using System.Text.Json;
using StormBench.Application.Alarms;
using StormBench.Application.Kpi;
using StormBench.Application.Scenarios;
using StormBench.Contracts;
using StormBench.Domain.Alarms;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;
using StormBench.Scada.Endpoints;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>
/// The JSON of the HTTP API stays as the recording scripts and the fault matrix read it: ids as
/// text, volumes as numbers, states in lower case, alarm states as numbers in the list and as
/// names in the journal.
/// </summary>
public class ApiJsonTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddDomainConverters();

    private readonly SiteFixture _site = new();

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    [Fact]
    public void Scenario_status_keeps_its_fields_and_their_order()
    {
        _site.SimBirth(
            SparkplugMetric.String(SimMetrics.State, ScenarioStates.Running),
            SparkplugMetric.Int64(SimMetrics.Run, 3),
            SparkplugMetric.String(SimMetrics.Storm, "s1"),
            SparkplugMetric.String(SimMetrics.Algorithm, "efd-center"),
            SparkplugMetric.Double(SimMetrics.Pace, 0.05),
            SparkplugMetric.Int64(SimMetrics.Step, 100),
            SparkplugMetric.Int64(SimMetrics.Steps, 400),
            SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(new DateTime(2000, 5, 30, 8, 20, 0))),
            SparkplugMetric.String(SimMetrics.StuckGate, "V2"),
            SparkplugMetric.String(SimMetrics.FrozenSensor, ""));

        using var json = JsonDocument.Parse(Serialize(ScenarioStatus.From(_site.Read())));
        var root = json.RootElement;

        Assert.Equal(
            ["simulator", "state", "run", "storm", "algorithm", "pace", "step", "steps", "clock", "message", "mismatches", "catalog",
             "centerLinkCut", "centerLinkUp", "stuckGate", "frozenSensor", "busy", "canStart", "canStop", "progress", "canInjectFaults"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("simulator").GetInt32());
        Assert.Equal("running", root.GetProperty("state").GetString());
        Assert.Equal(("s1", "efd-center", "V2"), (root.GetProperty("storm").GetString(), root.GetProperty("algorithm").GetString(), root.GetProperty("stuckGate").GetString()));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("frozenSensor").ValueKind);
        Assert.Equal("2000-05-30T08:20:00", root.GetProperty("clock").GetString());
        Assert.Equal(0.25, root.GetProperty("progress").GetDouble());
    }

    [Fact]
    public void The_catalog_is_written_as_the_simulator_published_it()
    {
        _site.SimBirth();

        var catalog = JsonDocument.Parse(Serialize(ScenarioStatus.From(_site.Read()))).RootElement.GetProperty("catalog");

        Assert.Equal(["storms", "algorithms", "paces", "defaultStorm", "defaultAlgorithm", "defaultPace"], catalog.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            """{"id":"s1","label":"Storm one","description":"Short","start":"2000-05-30T00:00:00","end":"2000-05-31T12:00:00","steps":431}""",
            catalog.GetProperty("storms")[0].GetRawText());
        var algorithm = catalog.GetProperty("algorithms")[1];
        Assert.Equal(["id", "label", "description", "mode", "center"], algorithm.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("efd-center", 3), (algorithm.GetProperty("id").GetString(), algorithm.GetProperty("mode").GetInt32()));
        Assert.Equal(JsonValueKind.Null, algorithm.GetProperty("center").ValueKind);
        Assert.Equal("s1", catalog.GetProperty("defaultStorm").GetString());
    }

    [Fact]
    public void A_run_summary_is_written_with_plain_ids_volumes_and_its_state()
    {
        var started = new DateTimeOffset(2026, 10, 17, 14, 25, 30, TimeSpan.Zero);
        var run = new RunSummary(
            new RunId("20261017-142530-3"), 3, new StormId("2000-05-30"), "30 May 2000", new AlgorithmId("efd-center"), "EFD from the center",
            0.05, RunState.Interrupted, new DateTime(2000, 5, 30), new DateTime(2000, 5, 31, 12, 0, 0), started, null,
            431, 431, new Volume(11_967.5), new Volume(12_931.0), -7.45, null, 0);

        Assert.Equal(
            """{"id":"20261017-142530-3","simRun":3,"storm":"2000-05-30","stormLabel":"30 May 2000","algorithm":"efd-center","algorithmLabel":"EFD from the center","paceSeconds":0.05,"state":"interrupted","stormStart":"2000-05-30T00:00:00","stormEnd":"2000-05-31T12:00:00","startedAt":"2026-10-17T14:25:30+00:00","finishedAt":null,"steps":431,"stepsPlanned":431,"overflow":11967.5,"baseCase":12931,"changePercent":-7.45,"wwtpInflow":null,"mismatches":0}""",
            Serialize(run));
    }

    [Fact]
    public void The_kpi_is_written_with_volumes_as_numbers_and_points_as_text()
    {
        _site.SimBirth(
            SparkplugMetric.Int64(SimMetrics.Run, 1),
            SparkplugMetric.Double(SimMetrics.OverflowTotal, 900.0),
            SparkplugMetric.Double(SimMetrics.BaseCaseOverflowTotal, 1_000.0),
            SparkplugMetric.Double(SimMetrics.WwtpInflow, 50.0),
            SparkplugMetric.Boolean(SimMetrics.OverflowActive("T1"), true));

        Assert.Equal(
            """{"total":900,"baseCase":1000,"delta":-100,"relative":-0.1,"comparison":1,"overflowing":["T1"],"points":3,"wwtpInflow":50}""",
            Serialize(SiteKpi.Overflow(_site.Read())));
    }

    [Fact]
    public void Alarms_carry_numeric_states_and_the_journal_their_names()
    {
        var at = new DateTimeOffset(2026, 10, 18, 17, 35, 50, TimeSpan.Zero);
        var definition = AlarmDefinitions.Overflow(new OverflowPointId("T1"), isTank: true);
        var alarm = new Alarm(definition, AlarmState.ReturnedUnacknowledged, at, new DateTime(2000, 5, 31, 7, 10, 0), null, at);
        var entry = new JournalEntry(52, new RunId("r"), definition.Id, definition.Source, definition.Message, definition.Priority,
            AlarmState.Unacknowledged, AlarmState.ReturnedUnacknowledged, at, null);

        Assert.Equal(
            """{"definition":{"id":"T1.Overflow","source":"T1","message":"Tank T1 overflowing","priority":1},"state":3,"activatedAt":"2026-10-18T17:35:50+00:00","simTime":"2000-05-31T07:10:00","acknowledgedAt":null,"returnedAt":"2026-10-18T17:35:50+00:00","active":false,"needsAcknowledge":true}""",
            Serialize(alarm));
        Assert.Equal(
            """{"id":52,"run":"r","alarmId":"T1.Overflow","source":"T1","message":"Tank T1 overflowing","priority":"High","from":"Unacknowledged","to":"ReturnedUnacknowledged","wallTime":"2026-10-18T17:35:50+00:00","simTime":null}""",
            Serialize(JournalEntryResponse.Of(entry)));
    }

    [Fact]
    public void A_refused_command_says_why()
    {
        Assert.Equal("""{"accepted":false,"reason":"No storm is running."}""", Serialize(CommandResult.Refused("No storm is running.")));
        Assert.Equal(
            """{"variant":"Published","lastAnsweredStep":-9223372036854775808,"proposals":0,"brokerConnected":true}""",
            Serialize(new Application.Optimization.OptimizationStatus("Published", long.MinValue, 0, true)));
    }
}
