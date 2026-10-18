using System.Collections.Immutable;
using CricketScorer.Api.Auth;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using CricketScorer.Api.Teams;
using CricketScorer.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Matches;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(AppDbContext db, CurrentUser current, MatchLoader loader) : ControllerBase
{
    public const int ShareCodeLength = 10;

    // Creates a match with its XIs and starts the creating device's scoring session.
    [HttpPost]
    public async Task<ActionResult<MatchCreatedDto>> Create(CreateMatchRequest request)
    {
        var schoolId = await current.RequireSchoolIdAsync();

        if (await db.Matches.SingleOrDefaultAsync(m => m.Id == request.Id) is { } existing)
        {
            if (existing.OwnerUserId != current.UserId) throw new ApiException(StatusCodes.Status409Conflict, "That match ID is already in use.");
            return Ok(new MatchCreatedDto(existing.Id, existing.ShareCode, existing.ActiveSessionId!.Value));
        }

        var teamIds = new[] { request.TeamAId, request.TeamBId };
        if (await db.Teams.CountAsync(t => teamIds.Contains(t.Id) && t.SchoolId == schoolId) != teamIds.Distinct().Count())
            throw new ApiException(StatusCodes.Status404NotFound, "Team not found.");
        if (request.TossWinnerTeamId is Guid toss && !teamIds.Contains(toss))
            throw new ApiException(StatusCodes.Status400BadRequest, "The toss winner must be one of the two teams.");

        var setup = new MatchSetup(BuildSettings(request),
            new TeamLineup(request.TeamAId, request.TeamAPlayers.ToImmutableList()),
            new TeamLineup(request.TeamBId, request.TeamBPlayers.ToImmutableList()));
        if (setup.Validate() is string problem) throw new ApiException(StatusCodes.Status400BadRequest, problem);
        await RequireSquadPlayersAsync(setup);

        var match = new Match
        {
            Id = request.Id,
            SchoolId = schoolId,
            OwnerUserId = current.UserId,
            ShareCode = await NewShareCodeAsync(),
            Date = request.Date,
            Venue = string.IsNullOrWhiteSpace(request.Venue) ? null : Names.Require(request.Venue, 100, "venue"),
            TeamAId = request.TeamAId,
            TeamBId = request.TeamBId,
            TossWinnerTeamId = request.TossWinnerTeamId,
            TossDecision = request.TossDecision,
            Format = setup.Settings.Format,
            OversPerSide = setup.Settings.OversPerSide,
            MaxOversPerBowler = setup.Settings.MaxOversPerBowler,
            BallsPerOver = setup.Settings.BallsPerOver,
            FreeHitsEnabled = setup.Settings.FreeHitsEnabled,
            DeclarationsAllowed = setup.Settings.DeclarationsAllowed,
            Status = MatchStatus.Upcoming,
            CreatedAt = DateTimeOffset.UtcNow,
            Players = [.. XiRows(request.Id, setup.TeamA), .. XiRows(request.Id, setup.TeamB)]
        };
        var session = NewSession(match.Id, request.DeviceLabel);
        match.ActiveSessionId = session.Id;
        db.Matches.Add(match);
        db.ScoringSessions.Add(session);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = match.Id }, new MatchCreatedDto(match.Id, match.ShareCode, session.Id));
    }

    // Lists the school's matches, newest first.
    [HttpGet]
    public async Task<List<MatchSummaryDto>> List()
    {
        var schoolId = await current.RequireSchoolIdAsync();
        var userId = current.UserId;
        return await (
            from m in db.Matches
            where m.SchoolId == schoolId
            join a in db.Teams on m.TeamAId equals a.Id
            join b in db.Teams on m.TeamBId equals b.Id
            join u in db.Users on m.OwnerUserId equals u.Id
            orderby m.Date descending, m.CreatedAt descending
            select new MatchSummaryDto(m.Id, m.Date, m.Venue, a.Name, b.Name, m.Status, u.DisplayName, m.OwnerUserId == userId)
        ).ToListAsync();
    }

    // Returns a match's setup and events; other scorers at the school get it read-only.
    [HttpGet("{id:guid}")]
    public async Task<MatchDetailDto> Get(Guid id)
    {
        var loaded = await loader.LoadAsync(id, await current.RequireSchoolIdAsync());
        var match = loaded.Match;
        var isOwner = match.OwnerUserId == current.UserId;
        var ownerName = await db.Users.Where(u => u.Id == match.OwnerUserId).Select(u => u.DisplayName).SingleAsync();
        return new MatchDetailDto(match.Id, match.ShareCode, match.Date, match.Venue,
            await LineupDtoAsync(loaded.Setup.TeamA), await LineupDtoAsync(loaded.Setup.TeamB),
            match.TossWinnerTeamId, match.TossDecision, loaded.Setup.Settings, match.Status,
            match.OwnerUserId, ownerName, isOwner, isOwner ? match.ActiveSessionId : null,
            EventDtos(loaded.Events));
    }

    // Starts a new scoring session on this device (owner only); the old device's session stops being accepted.
    [HttpPost("{id:guid}/sessions")]
    public async Task<ActionResult<SessionStartedDto>> StartSession(Guid id, StartSessionRequest request)
    {
        var loaded = await loader.LoadAsync(id, await current.RequireSchoolIdAsync());
        if (loaded.Match.OwnerUserId != current.UserId)
            throw new ApiException(StatusCodes.Status403Forbidden, "Only the scorer who created this match can score it.");
        var session = NewSession(id, request.DeviceLabel);
        db.ScoringSessions.Add(session);
        loaded.Match.ActiveSessionId = session.Id;
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new SessionStartedDto(session.Id, EventDtos(loaded.Events)));
    }

    // Converts stored events to the API's event shape, in order.
    public static List<EventDto> EventDtos(IEnumerable<StoredEvent> events) =>
        events.Select(e => new EventDto(e.Sequence, MatchLoader.Read(e))).ToList();

    // Creates a new scoring session for a device.
    public static ScoringSession NewSession(Guid matchId, string? deviceLabel) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        DeviceLabel = string.IsNullOrWhiteSpace(deviceLabel) ? null : deviceLabel.Trim()[..Math.Min(60, deviceLabel.Trim().Length)],
        StartedAt = DateTimeOffset.UtcNow
    };

    // Works out the match settings from the format's defaults plus any overrides, checking the ranges.
    private static MatchSettings BuildSettings(CreateMatchRequest r)
    {
        MatchSettings settings;
        try
        {
            settings = MatchSettings.ForFormat(r.Format, r.OversPerSide);
        }
        catch (ArgumentException ex)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, ex.Message);
        }

        if (settings.Format != MatchFormat.Timed)
            settings = settings with
            {
                OversPerSide = r.OversPerSide ?? settings.OversPerSide,
                MaxOversPerBowler = r.MaxOversPerBowler ?? settings.MaxOversPerBowler
            };
        settings = settings with
        {
            BallsPerOver = r.BallsPerOver ?? settings.BallsPerOver,
            FreeHitsEnabled = r.FreeHitsEnabled ?? settings.FreeHitsEnabled
        };

        if (settings.BallsPerOver is < 4 or > 10)
            throw new ApiException(StatusCodes.Status400BadRequest, "Balls per over must be between 4 and 10.");
        if (settings.OversPerSide is < 1 or > 100)
            throw new ApiException(StatusCodes.Status400BadRequest, "Overs per side must be between 1 and 100.");
        if (settings.MaxOversPerBowler is int max && (max < 1 || max > settings.OversPerSide))
            throw new ApiException(StatusCodes.Status400BadRequest, "A bowler's limit must be between 1 and the overs per side.");
        return settings;
    }

    // Refuses the XIs if any player isn't in that team's squad.
    private async Task RequireSquadPlayersAsync(MatchSetup setup)
    {
        var ids = setup.TeamA.Players.Concat(setup.TeamB.Players).ToList();
        var squads = await db.Players.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.TeamId);
        foreach (var lineup in new[] { setup.TeamA, setup.TeamB })
            if (lineup.Players.Any(id => !squads.TryGetValue(id, out var teamId) || teamId != lineup.TeamId))
                throw new ApiException(StatusCodes.Status400BadRequest, "Every player in an XI must be in that team's squad.");
    }

    // Builds the XI rows for one team, keeping the batting order.
    private static IEnumerable<MatchPlayer> XiRows(Guid matchId, TeamLineup lineup) =>
        lineup.Players.Select((id, i) => new MatchPlayer { MatchId = matchId, TeamId = lineup.TeamId, PlayerId = id, Order = i });

    // Builds a team's name and XI with player names, in batting order.
    private async Task<LineupDto> LineupDtoAsync(TeamLineup lineup)
    {
        var name = await db.Teams.Where(t => t.Id == lineup.TeamId).Select(t => t.Name).SingleAsync();
        var names = await db.Players.Where(p => lineup.Players.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        return new LineupDto(lineup.TeamId, name, lineup.Players.Select(id => new PlayerDto(id, names[id])).ToList());
    }

    // Makes a share code that no other match is using.
    private async Task<string> NewShareCodeAsync()
    {
        string code;
        do code = CodeGenerator.New(ShareCodeLength);
        while (await db.Matches.AnyAsync(m => m.ShareCode == code));
        return code;
    }
}
