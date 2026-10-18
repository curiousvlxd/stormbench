using StormBench.Application.Optimization;

namespace StormBench.Scada.Endpoints;

public static class OptimizationEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The central optimiser at a glance: its EFD variant, the last step it answered and its proposals so far.</summary>
        public IEndpointRouteBuilder MapOptimizationEndpoints()
        {
            app.MapGet("/api/optimization", (GetOptimizationStatusHandler status) => TypedResults.Ok(status.Handle()));
            return app;
        }
    }
}
