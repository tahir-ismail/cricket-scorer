namespace CricketScorer.Engine.Tests;

public class BowlingTests
{
    // Brings on the given bowler and bowls a full over of dots.
    private static MatchHarness Over(MatchHarness h, Guid bowler) => h.NewBowler(bowler).Dots(6);

    // Checks the bowler of the last over can't bowl the next one.
    [Fact]
    public void NextOverNeedsDifferentBowler()
    {
        var h = MatchHarness.Create().Start().Dots(6);
        Assert.NotNull(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[10])));
        Assert.Null(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[9])));
    }

    // Checks the bowler has to be from the fielding side.
    [Fact]
    public void BowlerMustBeOnFieldingSide()
    {
        var h = MatchHarness.Create().Start().Dots(6);
        Assert.NotNull(h.Try(new BowlerChanged(Guid.NewGuid(), h.A[5])));
    }

    // Checks a T20 bowler can't bowl a fifth over.
    [Fact]
    public void BowlerAtLimitRejected()
    {
        var h = MatchHarness.Create().Start().Dots(6);
        for (var over = 2; over <= 8; over++) Over(h, over % 2 == 0 ? h.B[9] : h.B[10]);
        Assert.Equal(24, h.Bowler(h.B[10]).LegalBalls);
        Assert.Equal(24, h.Bowler(h.B[9]).LegalBalls);
        Assert.NotNull(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[10])));
        Assert.Null(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[8])));
    }

    // Checks a timed match has no limit on overs per bowler.
    [Fact]
    public void TimedHasNoBowlerLimit()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed)).Start().Dots(6);
        for (var over = 2; over <= 30; over++) Over(h, over % 2 == 0 ? h.B[9] : h.B[10]);
        Assert.Equal(90, h.Bowler(h.B[10]).LegalBalls);
        Assert.Null(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[10])));
    }

    // Checks maidens: no runs off the bowler is a maiden, byes don't spoil it, wides do.
    [Fact]
    public void MaidenCountedWhenNoRunsOffBowler()
    {
        var h = MatchHarness.Create().Start().Dots(6);
        Assert.Equal(1, h.Bowler(h.B[10]).Maidens);

        h.NewBowler(h.B[9]).Dots(5).Bowl(extra: ExtraType.LegBye, extraRuns: 1);
        Assert.Equal(1, h.Bowler(h.B[9]).Maidens);

        h.NewBowler(h.B[10]).Dots(5).Bowl(extra: ExtraType.Wide).Dots(1);
        Assert.Equal(1, h.Bowler(h.B[10]).Maidens);
    }

    // Checks a bowler can be replaced mid-over and the over carries on.
    [Fact]
    public void MidOverChangeAllowed()
    {
        var h = MatchHarness.Create().Start().Dots(6);
        h.NewBowler(h.B[9]).Dots(3);
        Assert.NotNull(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[10])));
        h.NewBowler(h.B[8]).Dots(3);
        Assert.Equal(3, h.Bowler(h.B[9]).LegalBalls);
        Assert.Equal(3, h.Bowler(h.B[8]).LegalBalls);
        Assert.Equal(0, h.Bowler(h.B[9]).Maidens);
        Assert.Equal(0, h.Bowler(h.B[8]).Maidens);
        Assert.Equal(NextAction.ChooseBowler, h.State.Next);
        Assert.Equal(h.B[8], h.Inn.PreviousOverBowlerId);
    }

    // Checks the overs text for 3 overs and 2 balls.
    [Fact]
    public void BowlerOversText() => Assert.Equal("3.2", InningsState.OversText(20, 6));
}
