using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using CricketScorer.Api.Matches;
using CricketScorer.Engine;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Sync;

public sealed class SyncService(AppDbContext db)
{
    // Applies a batch of adds and undos in order, re-checking each event with the engine, and saves them in one go.
    public async Task<SyncResult> ApplyAsync(LoadedMatch loaded, Guid sessionId, IReadOnlyList<SyncOp> ops)
    {
        var match = loaded.Match;
        var rows = await db.Events.Where(e => e.MatchId == match.Id).ToListAsync();
        var events = loaded.Events.ToDictionary(e => e.Id, MatchLoader.Read);
        var addIds = ops.Where(o => o.Event is not null).Select(o => o.Event!.Id).ToList();
        var usedElsewhere = (await db.Events
            .Where(e => addIds.Contains(e.Id) && e.MatchId != match.Id)
            .Select(e => e.Id)
            .ToListAsync()).ToHashSet();

        var state = loaded.State;
        int applied = 0, skipped = 0;
        SyncRejection? rejected = null;

        for (var i = 0; i < ops.Count && rejected is null; i++)
        {
            var op = ops[i];
            var last = LastSequence(rows);
            string? reason;
            if (op.Kind == SyncKinds.Add)
            {
                var e = op.Event!;
                var seq = op.Sequence!.Value;
                var row = rows.Find(r => r.Id == e.Id);
                reason = usedElsewhere.Contains(e.Id) ? "That event ID is already used by another match."
                    : row is { IsRemoved: true } || row?.Sequence == seq ? null
                    : row is not null ? $"That event is already saved at position {row.Sequence}."
                    : seq != last + 1 ? $"Expected event {last + 1}."
                    : MatchEngine.Validate(state, e);
                if (reason is null && row is null)
                {
                    var stored = new StoredEvent
                    {
                        Id = e.Id, MatchId = match.Id, Sequence = seq, SessionId = sessionId, Type = e.GetType().Name,
                        Json = EventJson.Serialize(e), CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.Events.Add(stored);
                    rows.Add(stored);
                    events[e.Id] = e;
                    state = MatchEngine.Apply(state, e);
                    applied++;
                }
                else if (reason is null) skipped++;
                if (reason is not null) rejected = new SyncRejection(i, e.Id, reason);
            }
            else
            {
                var id = op.EventId!.Value;
                var row = rows.Find(r => r.Id == id);
                reason = row is null ? "Unknown event."
                    : row.IsRemoved ? null
                    : row.Sequence != last ? "Only the last event can be undone."
                    : null;
                if (reason is null && row is { IsRemoved: false })
                {
                    row.IsRemoved = true;
                    row.RemovedAt = DateTimeOffset.UtcNow;
                    state = MatchEngine.Replay(loaded.Setup,
                        rows.Where(r => !r.IsRemoved).OrderBy(r => r.Sequence).Select(r => events[r.Id]));
                    applied++;
                }
                else if (reason is null) skipped++;
                if (reason is not null) rejected = new SyncRejection(i, id, reason);
            }
        }

        match.Status = state.Closed ? MatchStatus.Finished
            : rows.Any(r => !r.IsRemoved) ? MatchStatus.Live
            : MatchStatus.Upcoming;
        db.Entry(match).Property(m => m.Status).IsModified = true;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "Another sync for this match happened at the same time. Try again.", "retry");
        }
        return new SyncResult(applied, skipped, LastSequence(rows), rejected);
    }

    // The sequence number of the last event that hasn't been undone, or 0.
    private static int LastSequence(List<StoredEvent> rows) =>
        rows.Where(r => !r.IsRemoved).Select(r => r.Sequence).DefaultIfEmpty(0).Max();
}

public static class SyncKinds
{
    public const string Add = "add";
    public const string Remove = "remove";
}
