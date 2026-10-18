using StormBench.Application.Scenarios;
using StormBench.Domain.Scenarios;

namespace StormBench.Scada.Endpoints;

public sealed record StartRequest(string Storm, string Algorithm, double PaceSeconds);

public static class ScenarioEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The test console: the simulator's status, start and stop of a storm.</summary>
        public IEndpointRouteBuilder MapScenarioEndpoints()
        {
            var api = app.MapGroup("/api/scenarios");
            api.MapGet("/", (GetScenarioStatusHandler status) => TypedResults.Ok(status.Handle()));
            api.MapPost("/start", async (StartRequest request, StartStormHandler start, CancellationToken ct) =>
                (await start.HandleAsync(new StartStorm(new StormId(request.Storm), new AlgorithmId(request.Algorithm), request.PaceSeconds), ct))
                .ToHttpResult());
            api.MapPost("/stop", async (StopStormHandler stop, CancellationToken ct) => (await stop.HandleAsync(ct)).ToHttpResult());
            return app;
        }
    }
}
