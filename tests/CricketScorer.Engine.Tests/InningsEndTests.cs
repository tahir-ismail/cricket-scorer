namespace CricketScorer.Engine.Tests;

public class InningsEndTests
{
    // Bowls full overs with the given runs off every ball, rotating through five fielders as bowlers.
    private static MatchHarness Overs(MatchHarness h, int overs, int runsPerBall = 0)
    {
        var fielding = h.State.Setup.LineupFor(h.Inn.BowlingTeamId)!.Players;
        for (var i = 0; i < overs; i++)
        {
            if (h.State.Next == NextAction.ChooseBowler)
                h.NewBowler(fielding[^(1 + h.Inn.LegalBalls / 6 % 5)]);
            for (var ball = 0; ball < 6 && h.State.Current is not null; ball++) h.Bowl(runsPerBall);
        }
        return h;
    }

    // Plays a full first innings of the given length scoring two a ball, then starts the chase.
    private static MatchHarness FirstInningsThenChase(MatchSettings settings, int overs)
    {
        var h = Overs(MatchHarness.Create(settings).Start(), overs, 2);
        if (h.State.Current is not null) h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual));
        return h.Start(teamABats: false);
    }

    // Checks a T20 innings ends after 120 legal balls.
    [Fact]
    public void OversCompleteEndsInnings()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.T20)).Start();
        for (var i = 0; i < 20; i++)
        {
            if (h.State.Next == NextAction.ChooseBowler) h.NewBowler(h.B[i % 5]);
            h.Dots(6);
        }
        Assert.Equal(120, h.Inn.LegalBalls);
        Assert.Equal(InningsEndReason.OversComplete, h.Inn.EndReason);
        Assert.Equal(NextAction.StartInnings, h.State.Next);
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid())));
    }

    // Checks the chasing side gets a target and the innings ends when it's reached.
    [Fact]
    public void ChaseShowsTargetAndEndsWhenReached()
    {
        var h = FirstInningsThenChase(MatchSettings.ForFormat(MatchFormat.CustomOvers, 2), 2);
        Assert.Equal(25, h.Inn.Target);
        h.Bowl(4).Bowl(4).Bowl(4).Bowl(4).Bowl(4).Bowl(4);
        Assert.Equal(24, h.Inn.Runs);
        Assert.Null(h.Inn.EndReason);
        h.NewBowler(h.A[^2]).Bowl(1);
        Assert.Equal(InningsEndReason.TargetReached, h.Inn.EndReason);
    }

    // Checks a wide that reaches the target ends the innings at once.
    [Fact]
    public void TargetReachedByWideEndsInnings()
    {
        var h = FirstInningsThenChase(MatchSettings.ForFormat(MatchFormat.CustomOvers, 5), 1);
        Assert.Equal(13, h.Inn.Target);
        h.Bowl(6, boundary: true).Bowl(6, boundary: true);
        h.Bowl(extra: ExtraType.Wide);
        Assert.Equal(13, h.Inn.Runs);
        Assert.Equal(InningsEndReason.TargetReached, h.Inn.EndReason);
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid())));
    }

    // Checks declaring is only allowed in timed matches.
    [Fact]
    public void DeclarationOnlyInTimed()
    {
        var t20 = MatchHarness.Create().Start().Bowl(1);
        Assert.NotNull(t20.Try(new InningsEnded(Guid.NewGuid(), InningsEndReason.Declared)));

        var timed = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed)).Start().Bowl(1);
        timed.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Declared));
        Assert.Equal(InningsEndReason.Declared, timed.State.Innings[0].EndReason);
    }

    // Checks the scorer can't end an innings for an automatic reason.
    [Fact]
    public void ScorerCantEndWithAutomaticReasons()
    {
        var h = MatchHarness.Create().Start();
        Assert.NotNull(h.Try(new InningsEnded(Guid.NewGuid(), InningsEndReason.OversComplete)));
        Assert.NotNull(h.Try(new InningsEnded(Guid.NewGuid(), InningsEndReason.TargetReached)));
        Assert.Null(h.Try(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)));
    }

    // Checks cutting the overs below what's been bowled ends the innings.
    [Fact]
    public void OversReducedBelowBowledEndsInnings()
    {
        var h = Overs(MatchHarness.Create().Start(), 12);
        h.Send(new OversChanged(Guid.NewGuid(), 10));
        Assert.Equal(10, h.Inn.OversLimit);
        Assert.Equal(InningsEndReason.OversComplete, h.Inn.EndReason);
    }

    // Checks cutting the overs recalculates the bowler limit.
    [Fact]
    public void OversReducedRecalculatesBowlerLimit()
    {
        var h = MatchHarness.Create().Start();
        h.Send(new OversChanged(Guid.NewGuid(), 10));
        Assert.Equal(2, h.Inn.MaxOversPerBowler);
        h.Dots(6).NewBowler(h.B[9]).Dots(6).NewBowler(h.B[10]).Dots(6).NewBowler(h.B[9]).Dots(6);
        Assert.Equal(12, h.Bowler(h.B[10]).LegalBalls);
        Assert.NotNull(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[10])));
        Assert.Null(h.Try(new BowlerChanged(Guid.NewGuid(), h.B[8])));
    }

    // Checks changing the overs isn't allowed in a timed match.
    [Fact]
    public void OversChangeRejectedInTimed()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed)).Start();
        Assert.NotNull(h.Try(new OversChanged(Guid.NewGuid(), 30)));
    }

    // Checks penalty runs to the batting side go into their extras.
    [Fact]
    public void PenaltyToBattingTeam()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        h.Send(new PenaltyRuns(Guid.NewGuid(), h.TeamA));
        Assert.Equal(6, h.Inn.Runs);
        Assert.Equal(5, h.Inn.Extras.Penalties);
    }

    // Checks penalty runs to the fielding side are added when they bat.
    [Fact]
    public void PenaltyToFieldingTeamCarriedToTheirInnings()
    {
        var h = MatchHarness.Create().Start();
        h.Send(new PenaltyRuns(Guid.NewGuid(), h.TeamB));
        Assert.Equal(0, h.Inn.Runs);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        Assert.Equal(5, h.Inn.Runs);
        Assert.Equal(5, h.Inn.Extras.Penalties);
    }

    // Checks penalty runs to a side that has already batted go on their last innings.
    [Fact]
    public void PenaltyToSideThatBattedGoesOnTheirInnings()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        h.Send(new PenaltyRuns(Guid.NewGuid(), h.TeamA));
        Assert.Equal(6, h.State.Innings[0].Runs);
        Assert.Equal(0, h.Inn.Runs);
    }

    // Checks the scorer can swap the batters by hand.
    [Fact]
    public void ManualSwapStrike()
    {
        var h = MatchHarness.Create().Start();
        h.Send(new StrikeSwapped(Guid.NewGuid()));
        Assert.Equal(h.A[1], h.Inn.StrikerId);
        Assert.Equal(h.A[0], h.Inn.NonStrikerId);
    }

    // Checks extra innings have no target unless the scorer sets one, and need two finished innings.
    [Fact]
    public void ExtraInningsHasNoTargetUnlessSet()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed)).Start().Bowl(4, boundary: true);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Declared));
        h.Start(teamABats: false).Bowl(6, boundary: true);
        Assert.Equal(5, h.Inn.Target);
        Assert.Equal(InningsEndReason.TargetReached, h.Inn.EndReason);

        h.Start();
        Assert.Equal(3, h.State.Innings.Count);
        Assert.Null(h.Inn.Target);
        h.Send(new TargetSet(Guid.NewGuid(), 50));
        Assert.Equal(50, h.Inn.Target);
    }
}
