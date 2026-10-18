using Microsoft.AspNetCore.Http.HttpResults;
using StormBench.Application.Runs;

namespace StormBench.Scada.Endpoints;

public static class StoreResults
{
    extension<T>(Task<T> read)
    {
        /// <summary>The result of a read from the run store, 503 with a problem when the store is not configured or not reachable.</summary>
        public async Task<Results<Ok<T>, ProblemHttpResult>> ToHttpResultAsync()
        {
            try
            {
                return TypedResults.Ok(await read);
            }
            catch (StoreUnavailableException ex)
            {
                return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
