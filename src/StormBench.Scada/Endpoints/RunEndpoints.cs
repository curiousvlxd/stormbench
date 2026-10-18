using StormBench.Application.Runs;

namespace StormBench.Scada.Endpoints;

public static class RunEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The runs of the test console, newest first.</summary>
        public IEndpointRouteBuilder MapRunEndpoints()
        {
            app.MapGet("/api/runs", (ListRunsHandler runs, CancellationToken ct) => runs.HandleAsync(ct).ToHttpResultAsync());
            return app;
        }
    }
}
