using System.Net;
using System.Net.Http.Json;
using CricketScorer.Api.Data;
using CricketScorer.Api.Matches;
using CricketScorer.Engine;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class MatchTests(ApiFactory factory)
{
    // Sends a create-match request.
    private static Task<HttpResponseMessage> Post(Scorer s, CreateMatchRequest r) =>
        s.Client.PostAsJsonAsync("/api/matches", r, TestApi.Json);

    // Reads a match's details.
    private static async Task<MatchDetailDto> Detail(Scorer s, Guid id) =>
        await (await s.Client.GetAsync($"/api/matches/{id}")).ReadAsync<MatchDetailDto>();

    // Creates two teams of the given size for the scorer and returns a match request between them.
    private static async Task<CreateMatchRequest> Request(Scorer s, int xi = 11)
    {
        var (teamA, a) = await s.CreateTeamAsync($"A {Guid.NewGuid():N}"[..20], xi);
        var (teamB, b) = await s.CreateTeamAsync($"B {Guid.NewGuid():N}"[..20], xi, isOpponent: true);
        return TestApi.MatchRequest(teamA, a, teamB, b);
    }

    // Checks a T20 match is created with the format's defaults and a 10-character share code.
    [Fact]
    public async Task CreateT20MatchUsesDefaults()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var match = await admin.CreateMatchAsync();
        var detail = await Detail(admin, match.Id);
        Assert.Equal(new MatchSettings(MatchFormat.T20, 20, 4, 6, true, false), detail.Settings);
        Assert.Equal(10, match.ShareCode.Length);
        Assert.Equal(MatchStatus.Upcoming, detail.Status);
        Assert.True(detail.IsOwner);
        Assert.Equal(match.SessionId, detail.ActiveSessionId);
        Assert.Equal(match.A, detail.TeamA.Players.Select(p => p.Id));
        Assert.Empty(detail.Events);
    }

    // Checks custom overs work out the bowler limit and overrides win.
    [Fact]
    public async Task CustomOversAndOverrides()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var thirty = await admin.CreateMatchAsync(format: MatchFormat.CustomOvers, overs: 30);
        Assert.Equal(6, (await Detail(admin, thirty.Id)).Settings.MaxOversPerBowler);

        var request = await Request(admin) with
        {
            Format = MatchFormat.CustomOvers, OversPerSide = 30, MaxOversPerBowler = 8, BallsPerOver = 8, FreeHitsEnabled = false
        };
        var created = await (await Post(admin, request)).ReadAsync<MatchCreatedDto>();
        var settings = (await Detail(admin, created.Id)).Settings;
        Assert.Equal((30, 8, 8, false), (settings.OversPerSide, settings.MaxOversPerBowler, settings.BallsPerOver, settings.FreeHitsEnabled));
    }

    // Checks a timed match has no overs limits even if overs are sent.
    [Fact]
    public async Task TimedMatchIgnoresOvers()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var request = await Request(admin) with { Format = MatchFormat.Timed, OversPerSide = 20, MaxOversPerBowler = 4 };
        var created = await (await Post(admin, request)).ReadAsync<MatchCreatedDto>();
        Assert.Equal(MatchSettings.ForFormat(MatchFormat.Timed), (await Detail(admin, created.Id)).Settings);
    }

    // Checks a player from another team can't be put in the XI.
    [Fact]
    public async Task PlayerFromWrongTeamIs400()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var request = await Request(admin);
        var (_, others) = await admin.CreateTeamAsync("Third team", 1);
        var bad = request with { TeamAPlayers = [.. request.TeamAPlayers.Take(10), others[0]] };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, bad)).StatusCode);
    }

    // Checks the engine's setup rules are enforced (XI of 1, same team twice, player listed twice).
    [Theory]
    [InlineData("xi1")]
    [InlineData("sameTeam")]
    [InlineData("duplicate")]
    public async Task EngineSetupRulesAre400(string kind)
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin);
        var bad = kind switch
        {
            "xi1" => r with { TeamAPlayers = [r.TeamAPlayers[0]] },
            "sameTeam" => r with { TeamBId = r.TeamAId, TeamBPlayers = r.TeamAPlayers },
            _ => r with { TeamAPlayers = [.. r.TeamAPlayers.Take(10), r.TeamAPlayers[0]] }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, bad)).StatusCode);
    }

    // Checks the toss winner must be one of the two teams.
    [Fact]
    public async Task TossWinnerMustBePlaying()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin) with { TossWinnerTeamId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, r)).StatusCode);
    }

    // Checks a team from another school can't be used.
    [Fact]
    public async Task OtherSchoolsTeamIs404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        var (theirTeam, theirPlayers) = await other.CreateTeamAsync("Glenwood U16A", 11);
        var r = await Request(admin) with { TeamBId = theirTeam, TeamBPlayers = theirPlayers };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, r)).StatusCode);
    }

    // Checks another scorer at the school can read the match but isn't the owner.
    [Fact]
    public async Task SchoolMateSeesMatchReadOnly()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var member = await factory.JoinAsync(admin);
        var match = await admin.CreateMatchAsync();
        var detail = await Detail(member, match.Id);
        Assert.False(detail.IsOwner);
        Assert.Null(detail.ActiveSessionId);
        Assert.Equal("Admin", detail.OwnerName);
        var list = await (await member.Client.GetAsync("/api/matches")).ReadAsync<List<MatchSummaryDto>>();
        var row = Assert.Single(list);
        Assert.False(row.IsOwner);
        Assert.Equal(MatchStatus.Upcoming, row.Status);
    }

    // Checks the match list is newest date first.
    [Fact]
    public async Task ListIsNewestFirst()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin);
        await Post(admin, r with { Id = Guid.NewGuid(), Date = new DateOnly(2026, 10, 1), Venue = "Old" });
        await Post(admin, r with { Id = Guid.NewGuid(), Date = new DateOnly(2026, 10, 20), Venue = "New" });
        var list = await (await admin.Client.GetAsync("/api/matches")).ReadAsync<List<MatchSummaryDto>>();
        Assert.Equal(["New", "Old"], list.Select(m => m.Venue));
    }

    // Checks another school can't see the match.
    [Fact]
    public async Task OtherSchoolGets404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var match = await admin.CreateMatchAsync();
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/matches/{match.Id}")).StatusCode);
        Assert.Empty(await (await other.Client.GetAsync("/api/matches")).ReadAsync<List<MatchSummaryDto>>());
    }

    // Checks sending the same create twice makes one match, and another scorer can't reuse the ID.
    [Fact]
    public async Task CreateWithSameIdTwiceIsIdempotent()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin);
        var first = await Post(admin, r);
        var second = await Post(admin, r);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.ReadAsync<MatchCreatedDto>(), await second.ReadAsync<MatchCreatedDto>());
        Assert.Single(await (await admin.Client.GetAsync("/api/matches")).ReadAsync<List<MatchSummaryDto>>());
        var member = await factory.JoinAsync(admin);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(member, r)).StatusCode);
    }

    // Checks out-of-range settings are refused.
    [Theory]
    [InlineData(3, null)]
    [InlineData(11, null)]
    [InlineData(6, 0)]
    [InlineData(6, 101)]
    public async Task BadSettingsAre400(int ballsPerOver, int? overs)
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin) with { BallsPerOver = ballsPerOver, OversPerSide = overs };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, r)).StatusCode);
    }

    // Checks custom overs without a number of overs is refused.
    [Fact]
    public async Task CustomOversNeedsOvers()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var r = await Request(admin) with { Format = MatchFormat.CustomOvers };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, r)).StatusCode);
    }
}
