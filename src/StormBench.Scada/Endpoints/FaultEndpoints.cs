using StormBench.Application.Scenarios;
using StormBench.Domain.Network;

namespace StormBench.Scada.Endpoints;

public sealed record CenterLinkRequest(bool Cut);

/// <summary>Target of a field fault: a valve or tank id, empty to clear.</summary>
public sealed record FaultTargetRequest(string? Target)
{
    public GateId? Gate => string.IsNullOrEmpty(Target) ? null : new GateId(Target);

    public TankId? Tank => string.IsNullOrEmpty(Target) ? null : new TankId(Target);
}

public static class FaultEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>Fault injection in the simulated field and on the link between the center and the PLC.</summary>
        public IEndpointRouteBuilder MapFaultEndpoints()
        {
            var api = app.MapGroup("/api/faults");
            api.MapPost("/center-link", async (CenterLinkRequest request, SetCenterLinkHandler link, CancellationToken ct) =>
                (await link.HandleAsync(new SetCenterLink(request.Cut), ct)).ToHttpResult());
            api.MapPost("/stuck-gate", async (FaultTargetRequest request, JamGateHandler jam, CancellationToken ct) =>
                (await jam.HandleAsync(new JamGate(request.Gate), ct)).ToHttpResult());
            api.MapPost("/frozen-sensor", async (FaultTargetRequest request, FreezeSensorHandler freeze, CancellationToken ct) =>
                (await freeze.HandleAsync(new FreezeSensor(request.Tank), ct)).ToHttpResult());
            return app;
        }
    }
}
