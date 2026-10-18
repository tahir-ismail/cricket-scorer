using System.Net;
using System.Net.Http.Json;
using CricketScorer.Api.Teams;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class TeamTests(ApiFactory factory)
{
    // Sends a create-team request.
    private static Task<HttpResponseMessage> PostTeam(Scorer s, Guid? id, string name, bool opponent = false) =>
        s.Client.PostAsJsonAsync("/api/teams", new CreateTeamRequest(id, name, opponent), TestApi.Json);

    // Checks a team and its squad are shared with every scorer in the school.
    [Fact]
    public async Task TeamsAreSharedWithinSchool()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var member = await factory.JoinAsync(admin);
        var (teamId, _) = await admin.CreateTeamAsync("Westville U16A", 3);
        var team = await (await member.Client.GetAsync($"/api/teams/{teamId}")).ReadAsync<TeamDto>();
        Assert.Equal(3, team.Players.Count);
        var update = await member.Client.PutAsJsonAsync($"/api/teams/{teamId}", new UpdateTeamRequest("Westville U16B", false), TestApi.Json);
        Assert.Equal("Westville U16B", (await update.ReadAsync<TeamDto>()).Name);
        var list = await (await admin.Client.GetAsync("/api/teams")).ReadAsync<List<TeamSummaryDto>>();
        Assert.Equal(3, Assert.Single(list).PlayerCount);
    }

    // Checks own teams are listed before opponents, each in name order.
    [Fact]
    public async Task OwnTeamsComeBeforeOpponents()
    {
        var admin = await factory.NewSchoolAdminAsync();
        await admin.CreateTeamAsync("Zulu XI", 0, isOpponent: true);
        await admin.CreateTeamAsync("U16B", 0);
        await admin.CreateTeamAsync("Alpha XI", 0, isOpponent: true);
        await admin.CreateTeamAsync("U16A", 0);
        var list = await (await admin.Client.GetAsync("/api/teams")).ReadAsync<List<TeamSummaryDto>>();
        Assert.Equal(["U16A", "U16B", "Alpha XI", "Zulu XI"], list.Select(t => t.Name));
    }

    // Checks another school can't see or edit a team.
    [Fact]
    public async Task OtherSchoolGets404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var (teamId, players) = await admin.CreateTeamAsync("Westville U16A", 1);
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/teams/{teamId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/teams/{teamId}", new UpdateTeamRequest("X", false), TestApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/teams/{teamId}/players", new CreatePlayerRequest(null, "X"), TestApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/players/{players[0]}", new UpdatePlayerRequest("X"), TestApi.Json)).StatusCode);
    }

    // Checks sending the same create twice with a client ID makes one team and one player.
    [Fact]
    public async Task CreateWithSameIdTwiceIsIdempotent()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await PostTeam(admin, id, "U15A")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostTeam(admin, id, "U15A")).StatusCode);
        var playerId = Guid.NewGuid();
        var first = await admin.Client.PostAsJsonAsync($"/api/teams/{id}/players", new CreatePlayerRequest(playerId, "Sipho"), TestApi.Json);
        var second = await admin.Client.PostAsJsonAsync($"/api/teams/{id}/players", new CreatePlayerRequest(playerId, "Sipho"), TestApi.Json);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var list = await (await admin.Client.GetAsync("/api/teams")).ReadAsync<List<TeamSummaryDto>>();
        Assert.Equal(1, Assert.Single(list).PlayerCount);
    }

    // Checks a client ID already used by another school is refused.
    [Fact]
    public async Task IdFromAnotherSchoolIs409()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var (teamId, players) = await admin.CreateTeamAsync("U14A", 1);
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.Conflict, (await PostTeam(other, teamId, "U14A")).StatusCode);
        var (otherTeam, _) = await other.CreateTeamAsync("U14B", 0);
        var player = await other.Client.PostAsJsonAsync($"/api/teams/{otherTeam}/players", new CreatePlayerRequest(players[0], "X"), TestApi.Json);
        Assert.Equal(HttpStatusCode.Conflict, player.StatusCode);
    }

    // Checks duplicate team names in one school are refused but allowed across schools.
    [Fact]
    public async Task DuplicateNameRules()
    {
        var admin = await factory.NewSchoolAdminAsync();
        await admin.CreateTeamAsync("U16A", 0);
        Assert.Equal(HttpStatusCode.Conflict, (await PostTeam(admin, null, " U16A ")).StatusCode);
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.Created, (await PostTeam(other, null, "U16A")).StatusCode);
    }

    // Checks blank names are refused.
    [Fact]
    public async Task BlankNamesAre400()
    {
        var admin = await factory.NewSchoolAdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostTeam(admin, null, "   ")).StatusCode);
        var (teamId, _) = await admin.CreateTeamAsync("U16A", 0);
        var player = await admin.Client.PostAsJsonAsync($"/api/teams/{teamId}/players", new CreatePlayerRequest(null, "  "), TestApi.Json);
        Assert.Equal(HttpStatusCode.BadRequest, player.StatusCode);
    }
}
