using StormBench.Infrastructure.PrimaryHost;

namespace StormBench.Scada.Endpoints;

public static class UnsEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The raw UNS as the Primary Host sees it: every node and device with its metrics.</summary>
        public IEndpointRouteBuilder MapUnsEndpoints()
        {
            app.MapGet("/api/uns", (UnsState state) => TypedResults.Ok(state.Snapshot()));
            return app;
        }
    }
}
