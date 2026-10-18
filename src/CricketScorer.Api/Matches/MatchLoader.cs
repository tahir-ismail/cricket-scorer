using System.Collections.Immutable;
using System.Text.Json;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using CricketScorer.Engine;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Matches;

public sealed record LoadedMatch(Match Match, MatchSetup Setup, List<StoredEvent> Events, MatchState State);

public sealed class MatchLoader(AppDbContext db)
{
    // Loads a match in the given school and rebuilds its state, or 404s.
    public async Task<LoadedMatch> LoadAsync(Guid matchId, Guid schoolId) =>
        await BuildAsync(await db.Matches.Include(m => m.Players)
            .SingleOrDefaultAsync(m => m.Id == matchId && m.SchoolId == schoolId));

    // Loads a match by its public share code and rebuilds its state, or 404s.
    public async Task<LoadedMatch> LoadByShareCodeAsync(string shareCode) =>
        await BuildAsync(await db.Matches.Include(m => m.Players)
            .SingleOrDefaultAsync(m => m.ShareCode == shareCode.Trim().ToUpper()));

    // Turns the stored match settings and XIs into the engine's match setup.
    public static MatchSetup SetupFor(Match match)
    {
        var settings = new MatchSettings(match.Format, match.OversPerSide, match.MaxOversPerBowler,
            match.BallsPerOver, match.FreeHitsEnabled, match.DeclarationsAllowed);
        return new MatchSetup(settings, Lineup(match, match.TeamAId), Lineup(match, match.TeamBId));
    }

    // Reads a stored event's JSON back into an engine event.
    public static MatchEvent Read(StoredEvent stored) =>
        JsonSerializer.Deserialize<MatchEvent>(stored.Json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    // Loads the match's live events in order and replays them through the engine.
    private async Task<LoadedMatch> BuildAsync(Match? match)
    {
        if (match is null) throw new ApiException(StatusCodes.Status404NotFound, "Match not found.");
        var events = await db.Events
            .Where(e => e.MatchId == match.Id && !e.IsRemoved)
            .OrderBy(e => e.Sequence)
            .ToListAsync();
        var setup = SetupFor(match);
        return new LoadedMatch(match, setup, events, MatchEngine.Replay(setup, events.Select(Read)));
    }

    // Builds one team's lineup in batting order.
    private static TeamLineup Lineup(Match match, Guid teamId) =>
        new(teamId, match.Players.Where(p => p.TeamId == teamId).OrderBy(p => p.Order).Select(p => p.PlayerId).ToImmutableList());
}
