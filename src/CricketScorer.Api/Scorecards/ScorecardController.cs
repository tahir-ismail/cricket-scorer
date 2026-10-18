using CricketScorer.Api.Auth;
using CricketScorer.Api.Data;
using CricketScorer.Api.Matches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Scorecards;

[ApiController]
public sealed class ScorecardController(AppDbContext db, CurrentUser current, MatchLoader loader) : ControllerBase
{
    // Returns the scorecard of a match in the caller's school.
    [HttpGet("api/matches/{id:guid}/scorecard")]
    public async Task<ScorecardDto> ForSchool(Guid id) =>
        await BuildAsync(await loader.LoadAsync(id, await current.RequireSchoolIdAsync()));

    // Returns the scorecard for a public share link; no login, and read-only.
    [AllowAnonymous]
    [HttpGet("api/share/{shareCode}/scorecard")]
    public async Task<ScorecardDto> ForShareLink(string shareCode) =>
        await BuildAsync(await loader.LoadByShareCodeAsync(shareCode));

    // Looks up the team and player names and builds the scorecard.
    private async Task<ScorecardDto> BuildAsync(LoadedMatch loaded)
    {
        var match = loaded.Match;
        var playerIds = match.Players.Select(p => p.PlayerId).ToList();
        var players = await db.Players.Where(p => playerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var teams = await db.Teams.Where(t => t.Id == match.TeamAId || t.Id == match.TeamBId).ToDictionaryAsync(t => t.Id, t => t.Name);
        var lastUpdated = await db.Events.Where(e => e.MatchId == match.Id)
            .MaxAsync(e => (DateTimeOffset?)(e.RemovedAt ?? e.CreatedAt));
        return ScorecardBuilder.Build(loaded, players, teams, lastUpdated);
    }
}
