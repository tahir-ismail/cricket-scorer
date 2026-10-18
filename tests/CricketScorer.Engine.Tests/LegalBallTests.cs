namespace CricketScorer.Engine.Tests;

public class LegalBallTests
{
    // Checks a single adds a run and swaps the batters.
    [Fact]
    public void SingleSwapsStrike()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        Assert.Equal(h.A[0], h.Inn.NonStrikerId);
        Assert.Equal(1, h.Inn.Runs);
        Assert.Equal(1, h.Inn.LegalBalls);
        Assert.Equal((1, 1), (h.Batter(h.A[0]).Runs, h.Batter(h.A[0]).BallsFaced));
        Assert.Equal((1, 1), (h.Bowler(h.B[10]).RunsConceded, h.Bowler(h.B[10]).LegalBalls));
    }

    // Checks two runs keep the same batter on strike.
    [Fact]
    public void TwoKeepsStrike()
    {
        var h = MatchHarness.Create().Start().Bowl(2);
        Assert.Equal(h.A[0], h.Inn.StrikerId);
        Assert.Equal(2, h.Inn.Runs);
    }

    // Checks three runs swap the batters.
    [Fact]
    public void ThreeSwaps()
    {
        var h = MatchHarness.Create().Start().Bowl(3);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
    }

    // Checks boundaries don't swap strike and are counted as fours and sixes.
    [Fact]
    public void BoundaryFourAndSixNoSwapAndCounted()
    {
        var h = MatchHarness.Create().Start().Bowl(4, boundary: true).Bowl(6, boundary: true);
        Assert.Equal(h.A[0], h.Inn.StrikerId);
        Assert.Equal(10, h.Inn.Runs);
        var b = h.Batter(h.A[0]);
        Assert.Equal((10, 2, 1, 1), (b.Runs, b.BallsFaced, b.Fours, b.Sixes));
    }

    // Checks six legal balls end the over, swap ends and ask for a new bowler.
    [Fact]
    public void SixLegalBallsEndOverSwapAndAskForBowler()
    {
        var h = MatchHarness.Create().Start().Dots(5);
        Assert.Equal(5, h.Inn.ThisOver.Count);
        h.Dots(1);
        Assert.Equal(6, h.Inn.LegalBalls);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        Assert.Null(h.Inn.BowlerId);
        Assert.Equal(h.B[10], h.Inn.PreviousOverBowlerId);
        Assert.Equal(NextAction.ChooseBowler, h.State.Next);
        Assert.Equal(6, h.Bowler(h.B[10]).LegalBalls);
        h.NewBowler(h.B[9]);
        Assert.Empty(h.Inn.ThisOver);
        Assert.Equal(NextAction.Bowl, h.State.Next);
    }

    // Checks a single off the last ball means the same batter faces the next over.
    [Fact]
    public void SingleOffLastBallMeansSameBatterFacesNextOver()
    {
        var h = MatchHarness.Create().Start().Dots(5).Bowl(1);
        Assert.Equal(h.A[0], h.Inn.StrikerId);
    }

    // Checks the short text shown for each kind of ball.
    [Theory]
    [InlineData(0, ExtraType.None, 0, false, false, "0")]
    [InlineData(1, ExtraType.None, 0, false, false, "1")]
    [InlineData(4, ExtraType.None, 0, true, false, "4")]
    [InlineData(6, ExtraType.None, 0, true, false, "6")]
    [InlineData(0, ExtraType.Wide, 0, false, false, "wd")]
    [InlineData(0, ExtraType.Wide, 2, false, false, "3wd")]
    [InlineData(0, ExtraType.NoBall, 0, false, false, "nb")]
    [InlineData(4, ExtraType.NoBall, 0, true, false, "nb+4")]
    [InlineData(0, ExtraType.Bye, 2, false, false, "2b")]
    [InlineData(0, ExtraType.LegBye, 1, false, false, "1lb")]
    [InlineData(0, ExtraType.None, 0, false, true, "W")]
    [InlineData(1, ExtraType.None, 0, false, true, "1W")]
    public void ChipTextCases(int bat, ExtraType extra, int extraRuns, bool boundary, bool wicket, string expected)
    {
        var d = wicket ? new Dismissal(DismissalType.RunOut, Guid.NewGuid()) : null;
        Assert.Equal(expected, BallRules.ChipText(new BallBowled(Guid.NewGuid(), bat, extra, extraRuns, boundary, d)));
    }

    // Checks impossible run values are rejected.
    [Fact]
    public void RejectsBadRunValues()
    {
        var h = MatchHarness.Create().Start();
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), BatRuns: 8)));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), BatRuns: -1)));
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), BatRuns: 3, Boundary: true)));
    }
}
