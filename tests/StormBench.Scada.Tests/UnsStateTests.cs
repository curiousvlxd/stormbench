using Microsoft.Extensions.Time.Testing;
using StormBench.Contracts;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

public class UnsStateTests
{
    private readonly UnsState _state = new(new FakeTimeProvider(DateTimeOffset.UnixEpoch));

    private static SparkplugTopic Topic(string type, string? device = null) =>
        new(Uns.GroupId, type, Uns.EdgeNodeId, device);

    private static SparkplugPayload Payload(ulong? seq, params SparkplugMetric[] metrics) => new(0, seq, metrics);

    private HostAction Birth(ulong bdSeq = 1)
    {
        _state.Apply(Topic(MessageTypes.NBirth), Payload(0, SparkplugMetric.Int64(PlcMetrics.BdSeq, (long)bdSeq)));
        return _state.Apply(
            Topic(MessageTypes.DBirth, Uns.PlcDeviceId),
            Payload(1, SparkplugMetric.Double(PlcMetrics.Level("T2"), 1.5), SparkplugMetric.Int64(PlcMetrics.StepReq, 7)));
    }

    private MetricSnapshot Level()
    {
        Assert.True(_state.TryGetDeviceMetric(Uns.GroupId, Uns.EdgeNodeId, Uns.PlcDeviceId, PlcMetrics.Level("T2"), out var m));
        return m;
    }

    [Fact]
    public void Birth_then_data_updates_values()
    {
        Assert.Equal(HostAction.None, Birth());
        var action = _state.Apply(
            Topic(MessageTypes.DData, Uns.PlcDeviceId), Payload(2, SparkplugMetric.Double(PlcMetrics.Level("T2"), 2.25)));

        Assert.Equal(HostAction.None, action);
        Assert.Equal(2.25, Level().Value);
        Assert.Equal(Quality.Good, Level().Quality);
    }

    [Fact]
    public void Node_death_with_matching_bdseq_marks_everything_stale()
    {
        Birth(bdSeq: 5);
        _state.Apply(Topic(MessageTypes.NDeath), Payload(null, SparkplugMetric.Int64(PlcMetrics.BdSeq, 5)));

        Assert.Equal(Quality.Stale, Level().Quality);
        Assert.False(_state.Snapshot().Single().Online);
    }

    [Fact]
    public void Death_certificate_of_an_earlier_session_is_ignored()
    {
        Birth(bdSeq: 6);
        _state.Apply(Topic(MessageTypes.NDeath), Payload(null, SparkplugMetric.Int64(PlcMetrics.BdSeq, 5)));

        Assert.Equal(Quality.Good, Level().Quality);
        Assert.True(_state.Snapshot().Single().Online);
    }

    [Fact]
    public void Sequence_gap_requests_rebirth_and_marks_stale()
    {
        Birth();
        var action = _state.Apply(
            Topic(MessageTypes.DData, Uns.PlcDeviceId), Payload(5, SparkplugMetric.Double(PlcMetrics.Level("T2"), 3.0)));

        Assert.Equal(HostAction.RequestRebirth, action);
        Assert.Equal(Quality.Stale, Level().Quality);
    }

    [Fact]
    public void Sequence_wraps_after_255()
    {
        _state.Apply(Topic(MessageTypes.NBirth), Payload(254, SparkplugMetric.Int64(PlcMetrics.BdSeq, 1)));
        Assert.Equal(HostAction.None, _state.Apply(Topic(MessageTypes.DBirth, Uns.PlcDeviceId), Payload(255)));
        Assert.Equal(HostAction.None, _state.Apply(Topic(MessageTypes.DData, Uns.PlcDeviceId), Payload(0)));
    }

    [Fact]
    public void Data_without_birth_requests_rebirth() =>
        Assert.Equal(
            HostAction.RequestRebirth,
            _state.Apply(Topic(MessageTypes.DData, Uns.PlcDeviceId), Payload(3, SparkplugMetric.Int64(PlcMetrics.StepReq, 1))));

    [Fact]
    public void Lost_broker_connection_marks_everything_stale()
    {
        Birth();
        _state.MarkAllStale();

        Assert.Equal(Quality.Stale, Level().Quality);
    }

    [Fact]
    public void Rebirth_after_death_restores_good_quality()
    {
        Birth(bdSeq: 1);
        _state.Apply(Topic(MessageTypes.NDeath), Payload(null, SparkplugMetric.Int64(PlcMetrics.BdSeq, 1)));
        Birth(bdSeq: 2);

        Assert.Equal(Quality.Good, Level().Quality);
        Assert.Equal(2UL, _state.Snapshot().Single().BdSeq);
    }

    [Fact]
    public void Host_state_json_round_trips()
    {
        var json = new HostState(true, 1_700_000_000_123).ToJson();
        Assert.Equal("{\"online\":true,\"timestamp\":1700000000123}", json);
        Assert.True(HostState.TryParse(json, out var parsed));
        Assert.Equal(new HostState(true, 1_700_000_000_123), parsed);
        Assert.False(HostState.TryParse("not json", out _));
    }
}
