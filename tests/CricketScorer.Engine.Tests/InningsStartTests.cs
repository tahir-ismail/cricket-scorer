namespace CricketScorer.Engine.Tests;

public class InningsStartTests
{
    // Checks a new match is waiting for the first innings.
    [Fact]
    public void NewMatchWaitsForInningsStart()
    {
        var h = MatchHarness.Create();
        Assert.Equal(NextAction.StartInnings, h.State.Next);
        Assert.Empty(h.State.Innings);
    }

    // Checks starting an innings sets the batters, bowler and limits.
    [Fact]
    public void StartingSetsBattersAndBowler()
    {
        var h = MatchHarness.Create().Start();
        Assert.Equal(h.A[0], h.Inn.StrikerId);
        Assert.Equal(h.A[1], h.Inn.NonStrikerId);
        Assert.Equal(h.B[10], h.Inn.BowlerId);
        Assert.Equal(h.TeamA, h.Inn.BattingTeamId);
        Assert.Equal(h.TeamB, h.Inn.BowlingTeamId);
        Assert.Equal(NextAction.Bowl, h.State.Next);
        Assert.Equal(20, h.Inn.OversLimit);
        Assert.Null(h.Inn.Target);
        Assert.Equal(2, h.Inn.Batters.Count(b => b.Status == BatterStatus.Batting));
    }

    // Checks invalid innings starts and balls before the start are rejected.
    [Fact]
    public void StartRejects()
    {
        var h = MatchHarness.Create();
        Assert.NotNull(h.Try(new InningsStarted(Guid.NewGuid(), h.TeamA, h.A[0], h.A[0], h.B[0])));
        Assert.NotNull(h.Try(new InningsStarted(Guid.NewGuid(), h.TeamA, h.B[0], h.A[1], h.B[1])));
        Assert.NotNull(h.Try(new InningsStarted(Guid.NewGuid(), h.TeamA, h.A[0], h.A[1], h.A[2])));
        Assert.NotNull(h.Try(new InningsStarted(Guid.NewGuid(), Guid.NewGuid(), h.A[0], h.A[1], h.B[0])));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid())));
    }

    // Checks Apply throws when the event isn't allowed.
    [Fact]
    public void ApplyThrowsOnInvalidEvent()
    {
        var h = MatchHarness.Create();
        Assert.Throws<InvalidEventException>(() => h.Bowl());
    }

    // Checks a batting order given at the start is used.
    [Fact]
    public void CustomBattingOrderIsUsed()
    {
        var h = MatchHarness.Create();
        var reversed = h.A.Reverse().ToImmutableList();
        h.Send(new InningsStarted(Guid.NewGuid(), h.TeamA, reversed[0], reversed[1], h.B[0], reversed));
        Assert.Equal(reversed, h.Inn.BattingOrder);
    }
}
