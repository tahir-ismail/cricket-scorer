namespace CricketScorer.Engine.Tests;

public class ExtrasTests
{
    // Checks a wide adds one run, isn't a legal ball and isn't faced.
    [Fact]
    public void WideAddsOneNotLegalNotFaced()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.Wide);
        Assert.Equal(1, h.Inn.Runs);
        Assert.Equal(0, h.Inn.LegalBalls);
        Assert.Equal(1, h.Inn.Extras.Wides);
        Assert.Equal(0, h.Batter(h.A[0]).BallsFaced);
        var bowler = h.Bowler(h.B[10]);
        Assert.Equal((1, 1, 0), (bowler.Wides, bowler.RunsConceded, bowler.LegalBalls));
        Assert.Equal(h.A[0], h.Inn.StrikerId);
    }

    // Checks a wide that runs to the boundary is five wides with no swap.
    [Fact]
    public void WideToBoundaryIsFiveWides()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.Wide, extraRuns: 4, boundary: true);
        Assert.Equal(5, h.Inn.Runs);
        Assert.Equal(5, h.Inn.Extras.Wides);
        Assert.Equal(5, h.Bowler(h.B[10]).RunsConceded);
        Assert.Equal(0, h.Batter(h.A[0]).BallsFaced);
        Assert.Equal(h.A[0], h.Inn.StrikerId);
        Assert.Equal("5wd", h.Inn.ThisOver[0]);
    }

    // Checks the batters swap when they run one on a wide.
    [Fact]
    public void WidesRunOneSwapsStrike()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.Wide, extraRuns: 1);
        Assert.Equal(2, h.Inn.Runs);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
    }

    // Checks a no-ball hit for four gives the batter four and the team five.
    [Fact]
    public void NoBallWithFourOffBat()
    {
        var h = MatchHarness.Create().Start().Bowl(4, ExtraType.NoBall, boundary: true);
        Assert.Equal(5, h.Inn.Runs);
        Assert.Equal(0, h.Inn.LegalBalls);
        Assert.Equal(1, h.Inn.Extras.NoBalls);
        var b = h.Batter(h.A[0]);
        Assert.Equal((4, 1, 1), (b.Runs, b.BallsFaced, b.Fours));
        var bowler = h.Bowler(h.B[10]);
        Assert.Equal((5, 1), (bowler.RunsConceded, bowler.NoBalls));
    }

    // Checks a no-ball gives a free hit when free hits are on.
    [Fact]
    public void NoBallGivesFreeHitWhenEnabled()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.NoBall);
        Assert.True(h.Inn.FreeHitNext);
        h.Bowl();
        Assert.False(h.Inn.FreeHitNext);
    }

    // Checks there's no free hit in a timed match.
    [Fact]
    public void NoFreeHitInTimedMatch()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed)).Start().Bowl(extra: ExtraType.NoBall);
        Assert.False(h.Inn.FreeHitNext);
    }

    // Checks a free hit carries over a wide until a legal ball.
    [Fact]
    public void FreeHitCarriesOverAWide()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.NoBall).Bowl(extra: ExtraType.Wide);
        Assert.True(h.Inn.FreeHitNext);
        h.Bowl(1);
        Assert.False(h.Inn.FreeHitNext);
    }

    // Checks byes and leg byes go to the team, not the batter or bowler.
    [Fact]
    public void ByesAndLegByes()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.Bye, extraRuns: 1);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        h.Bowl(extra: ExtraType.LegBye, extraRuns: 2);
        Assert.Equal(3, h.Inn.Runs);
        Assert.Equal(2, h.Inn.LegalBalls);
        Assert.Equal((1, 2), (h.Inn.Extras.Byes, h.Inn.Extras.LegByes));
        Assert.Equal((0, 1), (h.Batter(h.A[0]).Runs, h.Batter(h.A[0]).BallsFaced));
        Assert.Equal((0, 1), (h.Batter(h.A[1]).Runs, h.Batter(h.A[1]).BallsFaced));
        Assert.Equal(0, h.Bowler(h.B[10]).RunsConceded);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
    }

    // Checks byes run off a no-ball count as byes, not against the bowler.
    [Fact]
    public void ByesOffNoBall()
    {
        var h = MatchHarness.Create().Start().Bowl(extra: ExtraType.NoBall, extraRuns: 1);
        Assert.Equal(2, h.Inn.Runs);
        Assert.Equal((1, 1), (h.Inn.Extras.NoBalls, h.Inn.Extras.Byes));
        Assert.Equal(1, h.Bowler(h.B[10]).RunsConceded);
        Assert.Equal(h.A[1], h.Inn.StrikerId);
    }

    // Checks wides inside an over don't count towards its six balls.
    [Fact]
    public void SevenExtraBallsStillOnlySixLegal()
    {
        var h = MatchHarness.Create().Start().Dots(3).Bowl(extra: ExtraType.Wide).Bowl(extra: ExtraType.Wide).Dots(2);
        Assert.Equal(NextAction.Bowl, h.State.Next);
        h.Dots(1);
        Assert.Equal(NextAction.ChooseBowler, h.State.Next);
        Assert.Equal(6, h.Inn.LegalBalls);
    }
}
