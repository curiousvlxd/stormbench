using StormBench.Application.Optimization;
using StormBench.Application.Scenarios;
using StormBench.Contracts;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using StormBench.Sparkplug;

namespace StormBench.Infrastructure.PrimaryHost;

/// <summary>The test console's commands as NCMD to node Sim01.</summary>
public sealed class UnsSimulatorCommands(SparkplugHostApplication host) : ISimulatorCommands
{
    public Task StartStormAsync(StormId storm, AlgorithmId algorithm, double paceSeconds, CancellationToken cancellationToken) =>
        SendAsync(
            [
                SparkplugMetric.String(SimMetrics.Storm, storm.Value),
                SparkplugMetric.String(SimMetrics.Algorithm, algorithm.Value),
                SparkplugMetric.Double(SimMetrics.Pace, paceSeconds),
                SparkplugMetric.Boolean(SimMetrics.Start, true),
            ],
            cancellationToken);

    public Task StopStormAsync(CancellationToken cancellationToken) =>
        SendAsync([SparkplugMetric.Boolean(SimMetrics.Stop, true)], cancellationToken);

    public Task SetCenterLinkCutAsync(bool cut, CancellationToken cancellationToken) =>
        SendAsync([SparkplugMetric.Boolean(SimMetrics.CenterLinkCut, cut)], cancellationToken);

    public Task SetStuckGateAsync(GateId? gate, CancellationToken cancellationToken) =>
        SendAsync([SparkplugMetric.String(SimMetrics.StuckGate, gate?.Value ?? "")], cancellationToken);

    public Task SetFrozenSensorAsync(TankId? tank, CancellationToken cancellationToken) =>
        SendAsync([SparkplugMetric.String(SimMetrics.FrozenSensor, tank?.Value ?? "")], cancellationToken);

    private Task SendAsync(IReadOnlyList<SparkplugMetric> metrics, CancellationToken cancellationToken) =>
        host.SendNodeCommandAsync(Uns.GroupId, Uns.SimNodeId, metrics, cancellationToken);
}

/// <summary>The center's proposals as DCMD to device PLC01: a center setpoint per gate, then the step.</summary>
public sealed class UnsPlcCommands(SparkplugHostApplication host) : IPlcCommands
{
    public Task SendCenterProposalAsync(long lockstep, IReadOnlyList<(GateId Gate, GateOpening Setpoint)> proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return host.SendDeviceCommandAsync(
            Uns.GroupId,
            Uns.EdgeNodeId,
            Uns.PlcDeviceId,
            [
                .. proposal.Select(p => SparkplugMetric.Double(PlcMetrics.CenterSetpoint(p.Gate.Value), p.Setpoint.Fraction)),
                SparkplugMetric.Int64(PlcMetrics.CenterStep, lockstep),
            ],
            cancellationToken);
    }
}
