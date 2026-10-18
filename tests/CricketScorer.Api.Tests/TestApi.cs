using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CricketScorer.Api.Auth;
using CricketScorer.Api.Schools;
using CricketScorer.Api.Data;
using CricketScorer.Api.Matches;
using CricketScorer.Api.Sync;
using CricketScorer.Api.Teams;
using CricketScorer.Engine;

namespace CricketScorer.Api.Tests;

public sealed record Scorer(HttpClient Client, Guid UserId, string Email, Guid? SchoolId = null);

public sealed record TestMatch(Guid Id, string ShareCode, Guid SessionId, Guid TeamA, List<Guid> A, Guid TeamB, List<Guid> B);

public static class TestApi
{
    public const string Password = "long-enough-pw";

    public static readonly JsonSerializerOptions Json = EventJson.Options;

    // Registers a new scorer with a random email and returns a client that sends their token.
    public static async Task<Scorer> RegisterAsync(this ApiFactory factory, string name = "Scorer")
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, name), Json);
        response.EnsureSuccessStatusCode();
        var auth = await response.ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return new Scorer(client, auth.User.Id, email);
    }

    // Registers a scorer who creates a new school and becomes its admin.
    public static async Task<Scorer> NewSchoolAdminAsync(this ApiFactory factory, string school = "Westville")
    {
        var admin = await factory.RegisterAsync("Admin");
        var response = await admin.Client.PostAsJsonAsync("/api/schools", new CreateSchoolRequest(school), Json);
        response.EnsureSuccessStatusCode();
        return admin with { SchoolId = (await response.ReadAsync<SchoolDto>()).Id };
    }

    // Registers a scorer who joins the admin's school with its join code.
    public static async Task<Scorer> JoinAsync(this ApiFactory factory, Scorer admin)
    {
        var school = await (await admin.Client.GetAsync("/api/schools/me")).ReadAsync<SchoolDto>();
        var scorer = await factory.RegisterAsync("Member");
        var response = await scorer.Client.PostAsJsonAsync("/api/schools/join", new JoinSchoolRequest(school.JoinCode!), Json);
        response.EnsureSuccessStatusCode();
        return scorer with { SchoolId = school.Id };
    }

    // Creates a team with the given number of players and returns their IDs in squad order.
    public static async Task<(Guid TeamId, List<Guid> Players)> CreateTeamAsync(
        this Scorer scorer, string name, int players, bool isOpponent = false)
    {
        var response = await scorer.Client.PostAsJsonAsync("/api/teams", new CreateTeamRequest(null, name, isOpponent), Json);
        response.EnsureSuccessStatusCode();
        var team = await response.ReadAsync<TeamDto>();
        var ids = new List<Guid>();
        for (var i = 1; i <= players; i++)
        {
            var player = await scorer.Client.PostAsJsonAsync($"/api/teams/{team.Id}/players",
                new CreatePlayerRequest(null, $"{name} Player {i}"), Json);
            player.EnsureSuccessStatusCode();
            ids.Add((await player.ReadAsync<PlayerDto>()).Id);
        }
        return (team.Id, ids);
    }

    // Builds a match request for two teams, with every squad player in the XI in squad order.
    public static CreateMatchRequest MatchRequest(Guid teamA, List<Guid> a, Guid teamB, List<Guid> b,
        MatchFormat format = MatchFormat.T20, int? overs = null) =>
        new(Guid.NewGuid(), new DateOnly(2026, 10, 18), "Westville Oval", teamA, teamB, a, b, teamA, TossDecision.Bat,
            format, overs, null, null, null, "Test phone");

    // Creates two fresh teams (the second an opponent) and a match between them.
    public static async Task<TestMatch> CreateMatchAsync(this Scorer scorer, int xi = 11,
        MatchFormat format = MatchFormat.T20, int? overs = null)
    {
        var (teamA, a) = await scorer.CreateTeamAsync($"Home {Guid.NewGuid():N}"[..20], xi);
        var (teamB, b) = await scorer.CreateTeamAsync($"Away {Guid.NewGuid():N}"[..20], xi, isOpponent: true);
        var request = MatchRequest(teamA, a, teamB, b, format, overs);
        var response = await scorer.Client.PostAsJsonAsync("/api/matches", request, Json);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadAsync<MatchCreatedDto>();
        return new TestMatch(created.Id, created.ShareCode, created.SessionId, teamA, a, teamB, b);
    }

    // Sends sync operations for a match using the session it was created with.
    public static Task<HttpResponseMessage> SyncAsync(this Scorer scorer, TestMatch match, params SyncOp[] ops) =>
        scorer.SyncAsync(match, match.SessionId, ops);

    // Sends sync operations for a match using the given session.
    public static Task<HttpResponseMessage> SyncAsync(this Scorer scorer, TestMatch match, Guid sessionId, params SyncOp[] ops) =>
        scorer.Client.PostAsJsonAsync($"/api/matches/{match.Id}/sync", new SyncRequest(sessionId, [.. ops]), Json);

    // Builds an "add this event at this position" operation.
    public static SyncOp Add(int sequence, MatchEvent e) => new("add", sequence, e, null);

    // Builds an "undo this event" operation.
    public static SyncOp Remove(Guid eventId) => new("remove", null, null, eventId);

    // Builds the first innings start: team A's first two batters, team B's last player bowling.
    public static InningsStarted StartFor(TestMatch m) => new(Guid.NewGuid(), m.TeamA, m.A[0], m.A[1], m.B[^1]);

    // Builds a ball event.
    public static BallBowled Ball(int runs = 0, bool boundary = false) => new(Guid.NewGuid(), runs, Boundary: boundary);

    // Reads a JSON response body using the API's JSON settings.
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
