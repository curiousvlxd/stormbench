using Microsoft.AspNetCore.Http.HttpResults;
using StormBench.Application.Scenarios;

namespace StormBench.Scada.Endpoints;

public static class CommandResults
{
    extension(CommandResult result)
    {
        /// <summary>202 when the command went out to the UNS, 409 with the reason when it was refused.</summary>
        public Results<Accepted, Conflict<CommandResult>> ToHttpResult() =>
            result.Accepted ? TypedResults.Accepted((string?)null) : TypedResults.Conflict(result);
    }
}
