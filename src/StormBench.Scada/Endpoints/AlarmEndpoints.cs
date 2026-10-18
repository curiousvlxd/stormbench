using Microsoft.AspNetCore.Http.HttpResults;
using StormBench.Application.Alarms;
using StormBench.Domain.Runs;

namespace StormBench.Scada.Endpoints;

/// <summary>A journal entry on the wire: priority and states by name.</summary>
public sealed record JournalEntryResponse(
    long Id,
    RunId? Run,
    string AlarmId,
    string Source,
    string Message,
    string Priority,
    string From,
    string To,
    DateTimeOffset WallTime,
    DateTime? SimTime)
{
    public static JournalEntryResponse Of(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(
            entry.Id,
            entry.Run,
            entry.AlarmId,
            entry.Source,
            entry.Message,
            entry.Priority.ToString(),
            entry.From.ToString(),
            entry.To.ToString(),
            entry.WallTime,
            entry.SimTime);
    }
}

public static class AlarmEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>The alarm list, acknowledgement and the journal of the run store.</summary>
        public IEndpointRouteBuilder MapAlarmEndpoints()
        {
            var api = app.MapGroup("/api/alarms");
            api.MapGet("/", (AlarmEngine alarms) => TypedResults.Ok(alarms.Current()));
            api.MapPost("/{id}/acknowledge", Results<NoContent, NotFound> (string id, AcknowledgeAlarmHandler acknowledge) =>
                acknowledge.Handle(new AcknowledgeAlarm(id)) ? TypedResults.NoContent() : TypedResults.NotFound());
            api.MapGet("/journal", (string? run, ReadAlarmJournalHandler journal, CancellationToken ct) =>
                ReadAsync(journal, new ReadAlarmJournal(string.IsNullOrEmpty(run) ? null : new RunId(run)), ct).ToHttpResultAsync());
            return app;
        }
    }

    private static async Task<IReadOnlyList<JournalEntryResponse>> ReadAsync(
        ReadAlarmJournalHandler journal, ReadAlarmJournal query, CancellationToken cancellationToken) =>
        [.. (await journal.HandleAsync(query, cancellationToken)).Select(JournalEntryResponse.Of)];
}
