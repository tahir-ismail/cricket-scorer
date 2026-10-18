using CricketScorer.Api.Data;
using CricketScorer.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class SchemaTests(ApiFactory factory)
{
    // Opens a new database context on the test database.
    private AppDbContext NewContext() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    // Saves a user, school, two teams and a match straight into the database and returns the match ID.
    private async Task<Guid> SeedMatchAsync()
    {
        await using var db = NewContext();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"{Guid.NewGuid():N}@test.local", DisplayName = "Seed" };
        db.Add(user);
        await db.SaveChangesAsync();
        var school = new School { Id = Guid.NewGuid(), Name = "Seed School", JoinCode = Guid.NewGuid().ToString("N")[..8], AdminUserId = user.Id, CreatedAt = DateTimeOffset.UtcNow };
        user.SchoolId = school.Id;
        var teamA = new Team { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "A " + Guid.NewGuid() };
        var teamB = new Team { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "B " + Guid.NewGuid(), IsOpponent = true };
        var match = new Match
        {
            Id = Guid.NewGuid(), SchoolId = school.Id, OwnerUserId = user.Id, ShareCode = Guid.NewGuid().ToString("N")[..10],
            Date = new DateOnly(2026, 10, 18), TeamAId = teamA.Id, TeamBId = teamB.Id, Format = MatchFormat.T20,
            OversPerSide = 20, MaxOversPerBowler = 4, BallsPerOver = 6, FreeHitsEnabled = true, CreatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(school, teamA, teamB, match);
        await db.SaveChangesAsync();
        return match.Id;
    }

    // Builds an event row for a match at a sequence number.
    private static StoredEvent Event(Guid matchId, int sequence, bool removed = false) => new()
    {
        Id = Guid.NewGuid(), MatchId = matchId, Sequence = sequence, SessionId = Guid.NewGuid(), Type = "BallBowled",
        Json = "{}", IsRemoved = removed, CreatedAt = DateTimeOffset.UtcNow
    };

    // Checks two live events can't share a sequence number in the same match.
    [Fact]
    public async Task TwoActiveEventsCannotShareASequence()
    {
        var matchId = await SeedMatchAsync();
        await using var db = NewContext();
        db.Events.AddRange(Event(matchId, 1), Event(matchId, 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // Checks a removed event frees its sequence number for the next event.
    [Fact]
    public async Task RemovedEventFreesItsSequence()
    {
        var matchId = await SeedMatchAsync();
        await using var db = NewContext();
        db.Events.AddRange(Event(matchId, 1, removed: true), Event(matchId, 1));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Events.CountAsync(e => e.MatchId == matchId));
    }

    // Checks the match row version changes on every save.
    [Fact]
    public async Task MatchVersionChangesOnSave()
    {
        var matchId = await SeedMatchAsync();
        await using var db = NewContext();
        var match = await db.Matches.SingleAsync(m => m.Id == matchId);
        var before = match.Version;
        match.Venue = "Kings Park";
        await db.SaveChangesAsync();
        Assert.NotEqual(before, match.Version);
    }
}
