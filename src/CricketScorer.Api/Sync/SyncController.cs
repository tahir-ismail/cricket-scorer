using CricketScorer.Api.Auth;
using CricketScorer.Api.Infrastructure;
using CricketScorer.Api.Matches;
using Microsoft.AspNetCore.Mvc;

namespace CricketScorer.Api.Sync;

[ApiController]
[Route("api/matches/{id:guid}/sync")]
public sealed class SyncController(CurrentUser current, MatchLoader loader, SyncService sync) : ControllerBase
{
    public const int MaxOps = 500;

    // Saves the scoring device's events in order; resending the same events is harmless.
    [HttpPost]
    public async Task<SyncResult> Post(Guid id, SyncRequest request)
    {
        RequireValidBody(request);
        var loaded = await loader.LoadAsync(id, await current.RequireSchoolIdAsync());
        if (loaded.Match.OwnerUserId != current.UserId)
            throw new ApiException(StatusCodes.Status403Forbidden, "Only the scorer who created this match can score it.");
        if (loaded.Match.ActiveSessionId != request.SessionId)
            throw new ApiException(StatusCodes.Status409Conflict, "Another device has taken over scoring.", "session_replaced");
        return await sync.ApplyAsync(loaded, request.SessionId, request.Ops);
    }

    // Refuses a batch that is empty, too big, or has an operation missing its fields.
    private static void RequireValidBody(SyncRequest request)
    {
        if (request.Ops is not { Count: > 0 and <= MaxOps })
            throw new ApiException(StatusCodes.Status400BadRequest, $"Send between 1 and {MaxOps} operations.");
        foreach (var op in request.Ops)
        {
            var ok = op.Kind switch
            {
                SyncKinds.Add => op.Sequence is > 0 && op.Event is not null,
                SyncKinds.Remove => op.EventId is not null,
                _ => false
            };
            if (!ok) throw new ApiException(StatusCodes.Status400BadRequest,
                "Each operation must be \"add\" (with sequence and event) or \"remove\" (with eventId).");
        }
    }
}
