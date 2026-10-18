using CricketScorer.Api.Auth;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Teams;

[ApiController]
[Route("api/teams")]
public sealed class TeamsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    public const int NameLength = 100;

    // Lists the school's teams: its own first, then opponents, each by name.
    [HttpGet]
    public async Task<List<TeamSummaryDto>> List()
    {
        var schoolId = await current.RequireSchoolIdAsync();
        return await db.Teams
            .Where(t => t.SchoolId == schoolId)
            .OrderBy(t => t.IsOpponent).ThenBy(t => t.Name)
            .Select(t => new TeamSummaryDto(t.Id, t.Name, t.IsOpponent, t.Players.Count))
            .ToListAsync();
    }

    // Returns one team with its squad.
    [HttpGet("{id:guid}")]
    public async Task<TeamDto> Get(Guid id) => ToDto(await LoadAsync(id));

    // Creates a team; sending the same client ID again returns the existing team.
    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create(CreateTeamRequest request)
    {
        var schoolId = await current.RequireSchoolIdAsync();
        var name = Names.Require(request.Name, NameLength, "team");

        if (request.Id is Guid id && await db.Teams.Include(t => t.Players).SingleOrDefaultAsync(t => t.Id == id) is { } existing)
        {
            if (existing.SchoolId != schoolId) throw new ApiException(StatusCodes.Status409Conflict, "That team ID is already in use.");
            return Ok(ToDto(existing));
        }

        await RequireUniqueNameAsync(schoolId, name, null);
        var team = new Team { Id = request.Id ?? Guid.NewGuid(), SchoolId = schoolId, Name = name, IsOpponent = request.IsOpponent };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = team.Id }, ToDto(team));
    }

    // Renames a team or changes whether it's an opponent.
    [HttpPut("{id:guid}")]
    public async Task<TeamDto> Update(Guid id, UpdateTeamRequest request)
    {
        var team = await LoadAsync(id);
        var name = Names.Require(request.Name, NameLength, "team");
        await RequireUniqueNameAsync(team.SchoolId, name, team.Id);
        team.Name = name;
        team.IsOpponent = request.IsOpponent;
        await db.SaveChangesAsync();
        return ToDto(team);
    }

    // Adds a player to a team's squad; sending the same client ID again returns the existing player.
    [HttpPost("{teamId:guid}/players")]
    public async Task<ActionResult<PlayerDto>> AddPlayer(Guid teamId, CreatePlayerRequest request)
    {
        var team = await LoadAsync(teamId);
        var name = Names.Require(request.Name, PlayersController.NameLength, "player");

        if (request.Id is Guid id && await db.Players.SingleOrDefaultAsync(p => p.Id == id) is { } existing)
        {
            if (existing.TeamId != team.Id) throw new ApiException(StatusCodes.Status409Conflict, "That player ID is already in use.");
            return Ok(new PlayerDto(existing.Id, existing.Name));
        }

        var player = new Player { Id = request.Id ?? Guid.NewGuid(), TeamId = team.Id, Name = name };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return Created($"/api/players/{player.Id}", new PlayerDto(player.Id, player.Name));
    }

    // Loads a team in the caller's school with its players, or 404.
    private async Task<Team> LoadAsync(Guid id)
    {
        var schoolId = await current.RequireSchoolIdAsync();
        return await db.Teams.Include(t => t.Players).SingleOrDefaultAsync(t => t.Id == id && t.SchoolId == schoolId)
               ?? throw new ApiException(StatusCodes.Status404NotFound, "Team not found.");
    }

    // Refuses a name another team in the school already uses.
    private async Task RequireUniqueNameAsync(Guid schoolId, string name, Guid? exceptId)
    {
        if (await db.Teams.AnyAsync(t => t.SchoolId == schoolId && t.Name == name && t.Id != exceptId))
            throw new ApiException(StatusCodes.Status409Conflict, "A team with that name already exists.");
    }

    // Converts a team to the shape the API returns, players by name.
    private static TeamDto ToDto(Team team) =>
        new(team.Id, team.Name, team.IsOpponent,
            team.Players.OrderBy(p => p.Name).Select(p => new PlayerDto(p.Id, p.Name)).ToList());
}
