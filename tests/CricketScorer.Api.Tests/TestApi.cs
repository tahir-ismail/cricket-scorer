using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CricketScorer.Api.Auth;
using CricketScorer.Api.Schools;
using CricketScorer.Api.Teams;

namespace CricketScorer.Api.Tests;

public sealed record Scorer(HttpClient Client, Guid UserId, string Email, Guid? SchoolId = null);

public static class TestApi
{
    public const string Password = "long-enough-pw";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    // Reads a JSON response body using the API's JSON settings.
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
