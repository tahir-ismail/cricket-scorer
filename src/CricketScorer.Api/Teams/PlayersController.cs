using CricketScorer.Api.Auth;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Teams;

[ApiController]
[Route("api/players")]
public sealed class PlayersController(AppDbContext db, CurrentUser current) : ControllerBase
{
    public const int NameLength = 80;

    // Renames a player in the caller's school.
    [HttpPut("{id:guid}")]
    public async Task<PlayerDto> Update(Guid id, UpdatePlayerRequest request)
    {
        var schoolId = await current.RequireSchoolIdAsync();
        var player = await db.Players.SingleOrDefaultAsync(p =>
                         p.Id == id && db.Teams.Any(t => t.Id == p.TeamId && t.SchoolId == schoolId))
                     ?? throw new ApiException(StatusCodes.Status404NotFound, "Player not found.");
        player.Name = Names.Require(request.Name, NameLength, "player");
        await db.SaveChangesAsync();
        return new PlayerDto(player.Id, player.Name);
    }
}
