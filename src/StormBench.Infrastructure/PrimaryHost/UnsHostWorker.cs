using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StormBench.Application.Optimization;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Sparkplug.HostApplication;
using StormBench.Sparkplug.Payloads;
using StormBench.Sparkplug.Topics;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>
/// Runs the Sparkplug Primary Host session: every message updates the UNS state first, then the
/// central optimiser answers a new lockstep step of the PLC, then the site is evaluated (run
/// history, alarms, trends). Rebirth requests are rate limited per edge node.
/// </summary>
public sealed partial class UnsHostWorker(
    SparkplugHostApplication host,
    UnsState state,
    CenterOptimizer optimizer,
    SiteMonitor monitor,
    TimeProvider clock,
    ILogger<UnsHostWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RebirthBackoff = TimeSpan.FromSeconds(2);

    /// <summary>Conditions are also evaluated without traffic, e.g. the broker alarm after a reconnect.</summary>
    private static readonly TimeSpan EvaluationPeriod = TimeSpan.FromSeconds(1);

    private readonly Dictionary<(string, string), DateTimeOffset> _lastRebirthRequest = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        host.MessageReceived = OnMessageAsync;
        host.ConnectionLost = () =>
        {
            state.MarkAllStale();
            LogStale();
            monitor.Evaluate();
        };
        await Task.WhenAll(host.RunAsync(stoppingToken), EvaluatePeriodicallyAsync(stoppingToken));
    }

    private async Task EvaluatePeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(EvaluationPeriod, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                monitor.Evaluate();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task OnMessageAsync(SparkplugTopic topic, SparkplugPayload payload, CancellationToken cancellationToken)
    {
        if (state.Apply(topic, payload) is HostAction.RequestRebirth)
        {
            var key = (topic.GroupId, topic.EdgeNodeId);
            var now = clock.GetUtcNow();
            if (!_lastRebirthRequest.TryGetValue(key, out var last) || now - last > RebirthBackoff)
            {
                _lastRebirthRequest[key] = now;
                LogRebirth(topic.EdgeNodeId, topic.MessageType);
                await host.RequestRebirthAsync(topic.GroupId, topic.EdgeNodeId, cancellationToken);
            }
            monitor.Evaluate();
            return;
        }
        if (StepRequest(topic, payload) is { } step)
        {
            await optimizer.OnStepRequestedAsync(step, cancellationToken);
        }
        monitor.Evaluate();
    }

    /// <summary>The lockstep step a data or birth message of the PLC carries, if any.</summary>
    private static long? StepRequest(SparkplugTopic topic, SparkplugPayload payload) =>
        topic is { GroupId: Uns.GroupId, EdgeNodeId: Uns.EdgeNodeId, DeviceId: Uns.PlcDeviceId }
        && topic.MessageType is MessageTypes.DData or MessageTypes.DBirth
        && payload.Find(PlcMetrics.StepReq)?.Value is long step
            ? step
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broker connection lost: all UNS data marked stale")]
    private partial void LogStale();

    [LoggerMessage(Level = LogLevel.Information, Message = "Requesting rebirth of {EdgeNode} after unexpected {MessageType}")]
    private partial void LogRebirth(string edgeNode, string messageType);
}
