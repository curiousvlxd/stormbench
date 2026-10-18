using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using StormBench.Connectivity.Plc;
using StormBench.Contracts;
using StormBench.Sparkplug;

namespace StormBench.Connectivity;

/// <summary>
/// Edge gateway: the PLC becomes device PLC01 of Sparkplug edge node Edge01. Every register
/// image is compared with the last published one and only changed metrics go out as DDATA
/// (the scan counter at most once per second). Proposals from the central optimiser arrive as
/// DCMD and are written to the PLC's center block in one request.
/// </summary>
public sealed partial class EdgeGateway(
    IPlcSnapshotSource source,
    PlcModbusClient plc,
    SparkplugEdgeNode edge,
    IOptions<PlcOptions> plcOptions,
    GatewayMetrics metrics,
    TimeProvider clock,
    ILogger<EdgeGateway> logger) : BackgroundService
{
    private static readonly TimeSpan ScanCountPeriod = TimeSpan.FromSeconds(1);
    private readonly Dictionary<string, object?> _published = [];
    private long _lastStepReq = long.MinValue;
    private long _stepSeenAt;
    private DateTimeOffset _scanCountPublished = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        edge.DeviceCommandReceived = OnDeviceCommandAsync;
        var session = edge.RunAsync(stoppingToken);
        var poll = PollAsync(stoppingToken);
        await Task.WhenAll(session, poll);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var options = plcOptions.Value;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await source.EnsureConnectedAsync(cancellationToken);
                var snapshot = await source.ReadSnapshotAsync(cancellationToken);
                metrics.Polls.Add(1);
                var changed = Changed(snapshot);
                if (changed.Count > 0)
                {
                    await edge.PublishDeviceDataAsync(Uns.PlcDeviceId, changed, cancellationToken);
                    metrics.Published.Add(changed.Count);
                }
                if (options.PollInterval > TimeSpan.Zero)
                {
                    await Task.Delay(options.PollInterval, clock, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or FluentModbus.ModbusException or InvalidOperationException
                                           or Opc.Ua.ServiceResultException)
            {
                LogPlcUnreachable(source.Name, ex.Message);
                source.Disconnect();
                _published.Clear();
                await edge.PublishDeviceDeathAsync(Uns.PlcDeviceId, cancellationToken);
                try
                {
                    await Task.Delay(options.ReconnectDelay, clock, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Metrics that differ from what was published last (all of them for the first image).</summary>
    private List<SparkplugMetric> Changed(PlcSnapshot s)
    {
        if (s.StepReq != _lastStepReq)
        {
            _lastStepReq = s.StepReq;
            _stepSeenAt = Stopwatch.GetTimestamp();
        }
        List<SparkplugMetric> all =
        [
            SparkplugMetric.Int64(PlcMetrics.StepReq, s.StepReq),
            SparkplugMetric.Int64(PlcMetrics.StepAck, s.StepAck),
            SparkplugMetric.Int64(PlcMetrics.SimTime, s.SimTimeS),
            SparkplugMetric.Int64(PlcMetrics.CmdMode, (long)s.CmdMode),
            SparkplugMetric.Boolean(PlcMetrics.CenterOk, s.CenterOk),
            SparkplugMetric.Int64(PlcMetrics.ModeActive, (long)s.ModeActive),
            SparkplugMetric.Int64(PlcMetrics.Status, s.Status),
            SparkplugMetric.Int64(PlcMetrics.CenterStepEcho, s.CenterStep),
        ];
        for (var i = 0; i < PlcMetrics.Tanks.Count; i++)
        {
            all.Add(SparkplugMetric.Double(PlcMetrics.Level(PlcMetrics.Tanks[i]), s.Depths[i]));
        }
        for (var i = 0; i < PlcMetrics.ControlledValves.Count; i++)
        {
            all.Add(SparkplugMetric.Double(PlcMetrics.Setpoint(PlcMetrics.ControlledValves[i]), s.Setpoints[i]));
            all.Add(SparkplugMetric.Double(PlcMetrics.CenterSetpoint(PlcMetrics.ControlledValves[i]), s.CenterSetpoints[i]));
        }
        var now = clock.GetUtcNow();
        if (now - _scanCountPublished >= ScanCountPeriod || !_published.ContainsKey(PlcMetrics.ScanCount))
        {
            all.Add(SparkplugMetric.Int64(PlcMetrics.ScanCount, s.ScanCount));
            _scanCountPublished = now;
        }

        List<SparkplugMetric> changed = [];
        foreach (var metric in all)
        {
            if (!_published.TryGetValue(metric.Name, out var previous) || !Equals(previous, metric.Value))
            {
                _published[metric.Name] = metric.Value;
                changed.Add(metric);
            }
        }
        return changed;
    }

    private async Task OnDeviceCommandAsync(string deviceId, SparkplugPayload command, CancellationToken cancellationToken)
    {
        if (deviceId != Uns.PlcDeviceId)
        {
            return;
        }
        if (command.Find(PlcMetrics.CenterStep)?.Value is not long step)
        {
            LogRejected("no step");
            return;
        }
        var proposal = new double[PlcMetrics.ControlledValves.Count];
        for (var i = 0; i < proposal.Length; i++)
        {
            if (command.Find(PlcMetrics.CenterSetpoint(PlcMetrics.ControlledValves[i]))?.Value is not double value)
            {
                LogRejected($"missing proposal for {PlcMetrics.ControlledValves[i]}");
                return;
            }
            proposal[i] = value;
        }
        await plc.EnsureConnectedAsync(cancellationToken);
        await plc.WriteCenterProposalAsync(step, proposal, cancellationToken);
        metrics.Commands.Add(1);
        if (step == _lastStepReq)
        {
            metrics.CenterRoundTrip.Record(Stopwatch.GetElapsedTime(_stepSeenAt).TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PLC unreachable over {Driver}: {Reason}")]
    private partial void LogPlcUnreachable(string driver, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DCMD rejected: {Reason}")]
    private partial void LogRejected(string reason);
}

public sealed class GatewayMetrics
{
    public const string MeterName = "StormBench.Connectivity";

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        Polls = meter.CreateCounter<long>("stormbench.plc.polls", description: "Register images read from the PLC");
        Published = meter.CreateCounter<long>("stormbench.sparkplug.metrics_published", description: "Changed metrics published");
        Commands = meter.CreateCounter<long>("stormbench.center.proposals_written", description: "Center proposals written to the PLC");
        CenterRoundTrip = meter.CreateHistogram<double>(
            "stormbench.center.round_trip", unit: "ms", description: "New step seen at the PLC until the center's proposal is written back");
    }

    public Counter<long> Polls { get; }
    public Counter<long> Published { get; }
    public Counter<long> Commands { get; }
    public Histogram<double> CenterRoundTrip { get; }
}
