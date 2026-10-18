namespace CricketScorer.Engine.Tests;

public class WicketTests
{
    // Builds a dismissal of the given type.
    private static Dismissal Out(DismissalType type, Guid batter, Guid? fielder = null) => new(type, batter, fielder);

    // Checks bowled credits the bowler, records the wicket and asks for a new batter.
    [Fact]
    public void BowledCreditsBowlerAndAsksForBatter()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(d: Out(DismissalType.Bowled, h.A[0]));
        Assert.Equal(1, h.Inn.Wickets);
        var b = h.Batter(h.A[0]);
        Assert.Equal(BatterStatus.Out, b.Status);
        Assert.Equal(DismissalType.Bowled, b.Dismissal!.Type);
        Assert.Equal(h.B[10], b.DismissedByBowlerId);
        Assert.Equal(1, b.BallsFaced);
        Assert.Equal(1, h.Bowler(h.B[10]).Wickets);
        Assert.Equal(NextAction.ChooseBatter, h.State.Next);
        Assert.Equal(h.A[2], h.Inn.AvailableBatters.First());
        Assert.Equal("W", h.Inn.ThisOver[0]);
    }

    // Checks a catch needs a fielder from the fielding side.
    [Fact]
    public void CaughtNeedsFielder()
    {
        var h = MatchHarness.Create().Start();
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.Caught, h.A[0]))));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.Caught, h.A[0], h.A[5]))));
        Assert.Null(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.Caught, h.A[0], h.B[3]))));
    }

    // Checks caught and bowled is allowed with the bowler as fielder.
    [Fact]
    public void CaughtAndBowledFielderCanBeBowler()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(d: Out(DismissalType.Caught, h.A[0], h.B[10]));
        Assert.Equal(1, h.Bowler(h.B[10]).Wickets);
    }

    // Checks which dismissals can happen on which kinds of ball.
    [Theory]
    [InlineData(DismissalType.Bowled, ExtraType.None, false, true)]
    [InlineData(DismissalType.Bowled, ExtraType.Wide, false, false)]
    [InlineData(DismissalType.Stumped, ExtraType.Wide, false, true)]
    [InlineData(DismissalType.HitWicket, ExtraType.Wide, false, true)]
    [InlineData(DismissalType.Lbw, ExtraType.NoBall, false, false)]
    [InlineData(DismissalType.RunOut, ExtraType.NoBall, false, true)]
    [InlineData(DismissalType.Caught, ExtraType.Bye, false, false)]
    [InlineData(DismissalType.RunOut, ExtraType.LegBye, false, true)]
    [InlineData(DismissalType.Caught, ExtraType.None, true, false)]
    [InlineData(DismissalType.Bowled, ExtraType.None, true, false)]
    [InlineData(DismissalType.RunOut, ExtraType.None, true, true)]
    [InlineData(DismissalType.ObstructingField, ExtraType.None, true, true)]
    public void DismissalMatrix(DismissalType type, ExtraType extra, bool freeHit, bool allowed) =>
        Assert.Equal(allowed, DismissalRules.IsAllowed(type, extra, freeHit));

    // Checks the engine rejects a catch on a free hit.
    [Fact]
    public void CaughtOnFreeHitRejected()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.NoBall);
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.Caught, h.A[0], h.B[1]))));
    }

    // Checks only the striker can be bowled, but either batter can be run out.
    [Fact]
    public void OnlyStrikerCanBeBowledButEitherRunOut()
    {
        var h = MatchHarness.Create().Start();
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.Bowled, h.A[1]))));
        Assert.Null(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.RunOut, h.A[1]))));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), Dismissal: Out(DismissalType.RunOut, h.A[5]))));
    }

    // Checks a run out of the non-striker after one run.
    [Fact]
    public void RunOutNonStrikerAfterOneRun()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(1, d: Out(DismissalType.RunOut, h.A[1], h.B[2]));
        Assert.Equal(1, h.Inn.Runs);
        Assert.Equal(1, h.Batter(h.A[0]).Runs);
        Assert.Equal(BatterStatus.Out, h.Batter(h.A[1]).Status);
        Assert.Equal(BatterStatus.Batting, h.Batter(h.A[0]).Status);
        Assert.Null(h.Batter(h.A[1]).DismissedByBowlerId);
        Assert.Equal(0, h.Bowler(h.B[10]).Wickets);
        Assert.Equal("1W", h.Inn.ThisOver[0]);
    }

    // Checks the new batter goes to the dismissed batter's end.
    [Fact]
    public void NewBatterTakesDismissedBattersEnd()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(1, d: Out(DismissalType.RunOut, h.A[0]));
        h.In(h.A[2]);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        Assert.Equal(h.A[2], h.Inn.NonStrikerId);
        Assert.Equal(NextAction.Bowl, h.State.Next);
    }

    // Checks after a catch the new batter is on strike.
    [Fact]
    public void CaughtNewBatterOnStrike()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(d: Out(DismissalType.Caught, h.A[0], h.B[4])).In(h.A[2]);
        Assert.Equal(h.A[2], h.Inn.StrikerId);
        Assert.Equal(h.A[1], h.Inn.NonStrikerId);
    }

    // Checks a wicket on the last ball asks for the batter first, then the bowler.
    [Fact]
    public void WicketOnLastBallAsksForBatterThenBowler()
    {
        var h = MatchHarness.Create().Start().Dots(5);
        h.Bowl(d: Out(DismissalType.Bowled, h.A[0]));
        Assert.Equal(NextAction.ChooseBatter, h.State.Next);
        h.In(h.A[2]);
        Assert.Equal(NextAction.ChooseBowler, h.State.Next);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        Assert.Equal(h.A[2], h.Inn.NonStrikerId);
    }

    // Checks a player who has batted, or isn't in the team, can't come in.
    [Fact]
    public void BatterInRejectsPlayerWhoAlreadyBatted()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(d: Out(DismissalType.Bowled, h.A[0]));
        Assert.NotNull(h.Try(new BatterIn(Guid.NewGuid(), h.A[0])));
        Assert.NotNull(h.Try(new BatterIn(Guid.NewGuid(), h.A[1])));
        Assert.NotNull(h.Try(new BatterIn(Guid.NewGuid(), h.B[2])));
        Assert.Null(h.Try(new BatterIn(Guid.NewGuid(), h.A[7])));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid())));
    }

    // Checks the fall of wickets records the score and wicket number.
    [Fact]
    public void FallOfWicketsRecorded()
    {
        var h = MatchHarness.Create().Start().Bowl(4, boundary: true);
        h.Bowl(d: Out(DismissalType.Lbw, h.A[0])).In(h.A[2]).Bowl(2);
        h.Bowl(d: Out(DismissalType.Stumped, h.A[2], h.B[0]));
        Assert.Equal(2, h.Inn.FallOfWickets.Count);
        Assert.Equal(new FallOfWicket(1, 4, h.A[0], 2), h.Inn.FallOfWickets[0]);
        Assert.Equal(new FallOfWicket(2, 6, h.A[2], 4), h.Inn.FallOfWickets[1]);
    }
}
