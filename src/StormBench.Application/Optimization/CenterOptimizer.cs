using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormBench.Application.Site;
using StormBench.Domain.Control;
using StormBench.Domain.Network;

namespace StormBench.Application.Optimization;

/// <summary>Commands to the PLC (DCMD to PLC01).</summary>
public interface IPlcCommands
{
    /// <summary>The center's proposal for one lockstep step: a setpoint per station gate, in the PLC's order.</summary>
    Task SendCenterProposalAsync(long lockstep, IReadOnlyList<(GateId Gate, GateOpening Setpoint)> proposal, CancellationToken cancellationToken);
}

/// <summary>
/// Central optimiser for mode 3: when the PLC publishes a new lockstep step, compute Equal Filling
/// Degree from the UNS levels of that step and send the proposal to the PLC. The PLC stations
/// decide whether to use it. Stale or missing data produces no proposal: the PLC keeps waiting (or
/// runs locally if its link to the center is marked lost), which is the safe side. While the test
/// console runs an algorithm whose proposals come from another center (the simulator plays the
/// center for MPC, ADR 0017), this optimiser stays silent, so the PLC gets one proposal per step.
/// </summary>
public sealed partial class CenterOptimizer(
    ISiteReader site,
    IPlcCommands plc,
    IOptions<EfdParameters> parameters,
    OptimizationMetrics metrics,
    ILogger<CenterOptimizer> logger)
{
    private long _lastAnswered = long.MinValue;
    private long _proposals;

    public long LastAnsweredStep => Interlocked.Read(ref _lastAnswered);

    public long Proposals => Interlocked.Read(ref _proposals);

    public EfdVariant Variant => parameters.Value.Variant;

    /// <summary>The PLC asks for the step: propose once per step, when Scada is the center.</summary>
    public async Task OnStepRequestedAsync(long step, CancellationToken cancellationToken)
    {
        if (step == LastAnsweredStep)
        {
            return;
        }
        var snapshot = site.Read();
        var values = snapshot.PlcValues;
        if (values.CmdMode is not { Good: true, Value: ControlMode.EfdCenter }
            || values.CenterOk is not { Good: true, Value: true }
            || CenterRole.AnotherCenterProposes(snapshot.FieldValues.State, snapshot.FieldValues.Algorithm, snapshot.Catalog))
        {
            return;
        }

        var stations = values.Stations;
        var levels = new double[stations.Count];
        for (var i = 0; i < levels.Length; i++)
        {
            if (values.Level(stations[i].Tank) is not { Good: true } level)
            {
                LogNoData(step);
                return;
            }
            levels[i] = level.Value;
        }

        var proposal = EfdCoordinator.Propose(levels, parameters.Value);
        await plc.SendCenterProposalAsync(
            step, [.. stations.Select((s, i) => (s.Gate, new GateOpening(proposal[i])))], cancellationToken);
        Interlocked.Exchange(ref _lastAnswered, step);
        Interlocked.Increment(ref _proposals);
        metrics.Proposals.Add(1);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No proposal for step {Step}: level data missing or stale")]
    private partial void LogNoData(long step);
}

/// <summary>The central optimiser at a glance: its EFD variant, the last step it answered and its proposals so far.</summary>
public sealed record OptimizationStatus(string Variant, long LastAnsweredStep, long Proposals, bool BrokerConnected);

public sealed class GetOptimizationStatusHandler(CenterOptimizer optimizer, IBrokerConnection broker)
{
    public OptimizationStatus Handle() =>
        new(optimizer.Variant.ToString(), optimizer.LastAnsweredStep, optimizer.Proposals, broker.IsConnected);
}

public sealed class OptimizationMetrics
{
    public const string MeterName = "StormBench.Scada";

    public OptimizationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Proposals = meterFactory.Create(MeterName)
            .CreateCounter<long>("stormbench.optimization.proposals", description: "EFD proposals sent to the PLC");
    }

    public Counter<long> Proposals { get; }
}
