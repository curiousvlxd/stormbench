using Microsoft.Extensions.Logging;
using StormBench.Application.Site;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;

namespace StormBench.Application.Scenarios;

/// <summary>The outcome of a console command: accepted means it went out to the simulator, not that the simulator ran it.</summary>
public sealed record CommandResult(bool Accepted, string? Reason)
{
    public static CommandResult Ok { get; } = new(true, null);

    public static CommandResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// The commands the simulator takes. Like every command they travel through the UNS (NCMD to
/// Sim01); the simulator decides and reports the outcome in its scenario state and message.
/// </summary>
public interface ISimulatorCommands
{
    Task StartStormAsync(StormId storm, AlgorithmId algorithm, double paceSeconds, CancellationToken cancellationToken);

    Task StopStormAsync(CancellationToken cancellationToken);

    /// <summary>Cut (or restore) the link between the center and the PLC.</summary>
    Task SetCenterLinkCutAsync(bool cut, CancellationToken cancellationToken);

    /// <summary>The gate keeps its opening whatever the PLC commands; null frees it.</summary>
    Task SetStuckGateAsync(GateId? gate, CancellationToken cancellationToken);

    /// <summary>The tank's level reading keeps its value; null unfreezes it.</summary>
    Task SetFrozenSensorAsync(TankId? tank, CancellationToken cancellationToken);
}

/// <summary>
/// Test console of the testbed. The checks here only save a round trip: the simulator would
/// refuse the same commands.
/// </summary>
public static class TestConsole
{
    /// <summary>Why no command can reach the simulator now, or null when one can.</summary>
    public static string? Unavailable(IBrokerConnection broker, ScenarioStatus status)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(status);
        return !broker.IsConnected ? "Scada has no broker connection."
            : status.Simulator is not Presence.Online ? "The simulator is offline."
            : null;
    }
}

public sealed record StartStorm(StormId Storm, AlgorithmId Algorithm, double PaceSeconds);

/// <summary>Asks the simulator to run a storm of its catalog with a control algorithm at a pace.</summary>
public sealed partial class StartStormHandler(
    ISiteReader site, IBrokerConnection broker, ISimulatorCommands simulator, ILogger<StartStormHandler> logger)
{
    public async Task<CommandResult> HandleAsync(StartStorm command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var status = ScenarioStatus.From(site.Read());
        if (TestConsole.Unavailable(broker, status) is { } reason)
        {
            return CommandResult.Refused(reason);
        }
        if (status.Busy)
        {
            return CommandResult.Refused("A storm is already running.");
        }
        if (status.Catalog is not { } catalog)
        {
            return CommandResult.Refused("The simulator has not published its catalog yet.");
        }
        if (catalog.Storm(command.Storm) is null)
        {
            return CommandResult.Refused($"Unknown storm '{command.Storm}'.");
        }
        if (catalog.Algorithm(command.Algorithm) is null)
        {
            return CommandResult.Refused($"Unknown algorithm '{command.Algorithm}'.");
        }
        if (!StormPace.IsValid(command.PaceSeconds))
        {
            return CommandResult.Refused($"Pace must be between 0 and {StormPace.MaxSeconds} s per step.");
        }
        await simulator.StartStormAsync(command.Storm, command.Algorithm, command.PaceSeconds, cancellationToken);
        LogStart(command.Storm.Value, command.Algorithm.Value, command.PaceSeconds);
        return CommandResult.Ok;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Console: start storm {Storm} with {Algorithm}, {Pace} s per step")]
    private partial void LogStart(string storm, string algorithm, double pace);
}

/// <summary>Asks the simulator to stop the running storm.</summary>
public sealed partial class StopStormHandler(
    ISiteReader site, IBrokerConnection broker, ISimulatorCommands simulator, ILogger<StopStormHandler> logger)
{
    public async Task<CommandResult> HandleAsync(CancellationToken cancellationToken)
    {
        var status = ScenarioStatus.From(site.Read());
        if (TestConsole.Unavailable(broker, status) is { } reason)
        {
            return CommandResult.Refused(reason);
        }
        if (!status.Busy)
        {
            return CommandResult.Refused("No storm is running.");
        }
        await simulator.StopStormAsync(cancellationToken);
        LogStop();
        return CommandResult.Ok;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Console: stop the running storm")]
    private partial void LogStop();
}

/// <summary>Cut the link between the center and the PLC (true), or restore it (false).</summary>
public sealed record SetCenterLink(bool Cut);

/// <summary>
/// Fault injection: with the link cut the simulator hands the PLC its steps with CenterOk = 0
/// and the stations run locally.
/// </summary>
public sealed partial class SetCenterLinkHandler(
    ISiteReader site, IBrokerConnection broker, ISimulatorCommands simulator, ILogger<SetCenterLinkHandler> logger)
{
    public async Task<CommandResult> HandleAsync(SetCenterLink command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (TestConsole.Unavailable(broker, ScenarioStatus.From(site.Read())) is { } reason)
        {
            return CommandResult.Refused(reason);
        }
        await simulator.SetCenterLinkCutAsync(command.Cut, cancellationToken);
        LogCenterLink(command.Cut ? "cut" : "restored");
        return CommandResult.Ok;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Console: link to the center {Action}")]
    private partial void LogCenterLink(string action);
}

/// <summary>Jam a controlled gate of the simulated field; null frees the jammed one.</summary>
public sealed record JamGate(GateId? Gate);

public sealed class JamGateHandler(
    ISiteReader site, IBrokerConnection broker, ISimulatorCommands simulator, ILogger<JamGateHandler> logger)
{
    public async Task<CommandResult> HandleAsync(JamGate command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var snapshot = site.Read();
        if (TestConsole.Unavailable(broker, ScenarioStatus.From(snapshot)) is { } reason)
        {
            return CommandResult.Refused(reason);
        }
        if (command.Gate is { } gate && snapshot.Network?.HasGate(gate) is not true)
        {
            return CommandResult.Refused($"Unknown target '{gate}'.");
        }
        await simulator.SetStuckGateAsync(command.Gate, cancellationToken);
        FieldFaultLog.Fault(logger, "stuck gate", command.Gate?.Value ?? "cleared");
        return CommandResult.Ok;
    }
}

/// <summary>Freeze the level reading of a tank of the simulated field; null unfreezes the frozen one.</summary>
public sealed record FreezeSensor(TankId? Tank);

public sealed class FreezeSensorHandler(
    ISiteReader site, IBrokerConnection broker, ISimulatorCommands simulator, ILogger<FreezeSensorHandler> logger)
{
    public async Task<CommandResult> HandleAsync(FreezeSensor command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var snapshot = site.Read();
        if (TestConsole.Unavailable(broker, ScenarioStatus.From(snapshot)) is { } reason)
        {
            return CommandResult.Refused(reason);
        }
        if (command.Tank is { } tank && snapshot.Network?.HasTank(tank) is not true)
        {
            return CommandResult.Refused($"Unknown target '{tank}'.");
        }
        await simulator.SetFrozenSensorAsync(command.Tank, cancellationToken);
        FieldFaultLog.Fault(logger, "frozen sensor", command.Tank?.Value ?? "cleared");
        return CommandResult.Ok;
    }
}

internal static partial class FieldFaultLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Console: fault {Fault} {Target}")]
    public static partial void Fault(ILogger logger, string fault, string target);
}
