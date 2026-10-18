namespace StormBench.Scada.Endpoints;

public static class ScadaApi
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The HTTP API under <c>/api</c>: what the recording scripts, the fault matrix and Grafana read.</summary>
        public IEndpointRouteBuilder MapScadaApi() => app
            .MapUnsEndpoints()
            .MapOptimizationEndpoints()
            .MapScenarioEndpoints()
            .MapFaultEndpoints()
            .MapKpiEndpoints()
            .MapAlarmEndpoints()
            .MapRunEndpoints();
    }
}
