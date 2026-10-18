using StormBench.Application.Kpi;

namespace StormBench.Scada.Endpoints;

public static class KpiEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>Overflow of the running storm against its Base Case.</summary>
        public IEndpointRouteBuilder MapKpiEndpoints()
        {
            app.MapGet("/api/kpi", (GetKpiHandler kpi) => TypedResults.Ok(kpi.Handle()));
            return app;
        }
    }
}
