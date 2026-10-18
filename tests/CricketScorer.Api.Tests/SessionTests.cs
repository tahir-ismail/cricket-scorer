using System.Net;
using System.Net.Http.Json;
using CricketScorer.Api.Data;
using CricketScorer.Api.Matches;
using CricketScorer.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class SessionTests(ApiFactory factory)
{
    // Saves events straight into the database, numbered from 1, because the sync endpoint comes later.
    private async Task SeedEventsAsync(TestMatch match, params MatchEvent[] events)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Events.AddRange(events.Select((e, i) => new StoredEvent
        {
            Id = e.Id, MatchId = match.Id, Sequence = i + 1, SessionId = match.SessionId, Type = e.GetType().Name,
            Json = EventJson.Serialize(e), CreatedAt = DateTimeOffset.UtcNow
        }));
        await db.SaveChangesAsync();
    }

    // Asks to take over scoring a match.
    private static Task<HttpResponseMessage> TakeOver(Scorer s, Guid matchId) =>
        s.Client.PostAsJsonAsync($"/api/matches/{matchId}/sessions", new StartSessionRequest("iPad"), TestApi.Json);

    // Checks taking over gives a new session and the existing events.
    [Fact]
    public async Task TakeoverStartsNewSessionAndReturnsEvents()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var match = await admin.CreateMatchAsync();
        var start = new InningsStarted(Guid.NewGuid(), match.TeamA, match.A[0], match.A[1], match.B[^1]);
        await SeedEventsAsync(match, start, new BallBowled(Guid.NewGuid(), 4, Boundary: true));

        var response = await TakeOver(admin, match.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.ReadAsync<SessionStartedDto>();
        Assert.NotEqual(match.SessionId, session.SessionId);
        Assert.Equal([1, 2], session.Events.Select(e => e.Sequence));
        Assert.Equal(4, Assert.IsType<BallBowled>(session.Events[1].Event).BatRuns);

        var detail = await (await admin.Client.GetAsync($"/api/matches/{match.Id}")).ReadAsync<MatchDetailDto>();
        Assert.Equal(session.SessionId, detail.ActiveSessionId);
        Assert.Equal(2, detail.Events.Count);
    }

    // Checks only the owner can start a scoring session.
    [Fact]
    public async Task SchoolMateCantTakeOver()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var member = await factory.JoinAsync(admin);
        var match = await admin.CreateMatchAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await TakeOver(member, match.Id)).StatusCode);
    }

    // Checks another school can't take over the match.
    [Fact]
    public async Task OtherSchoolGets404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var match = await admin.CreateMatchAsync();
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.NotFound, (await TakeOver(other, match.Id)).StatusCode);
    }
}
