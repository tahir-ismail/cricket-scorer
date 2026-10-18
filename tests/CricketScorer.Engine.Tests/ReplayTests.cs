namespace CricketScorer.Engine.Tests;

public class ReplayTests
{
    // Takes a snapshot of the parts of the current innings that undo must restore.
    private static object Snapshot(MatchState s)
    {
        var inn = s.Innings[^1];
        return (s.Innings.Count, inn.Runs, inn.Wickets, inn.LegalBalls, s.Next, inn.StrikerId, inn.NonStrikerId,
            inn.BowlerId, string.Join(",", inn.ThisOver), inn.EndReason, inn.Extras,
            string.Join(",", inn.Batters), string.Join(",", inn.Bowlers), s.Result);
    }

    // Plays a short scripted match: 2 overs a side, 4 players a side, with every kind of event.
    private static MatchHarness ScriptedMatch()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 2), xiSize: 4);
        var (a, b) = (h.A, h.B);

        h.Send(new InningsStarted(Guid.NewGuid(), h.TeamA, a[0], a[1], b[3]));
        h.Bowl(4, boundary: true)
            .Bowl(extra: ExtraType.Wide)
            .Bowl(1)
            .Bowl(4, ExtraType.NoBall, boundary: true)
            .Bowl(d: new Dismissal(DismissalType.RunOut, a[1], b[0])).In(a[2])
            .Bowl(extra: ExtraType.Bye, extraRuns: 1)
            .Dots(1)
            .Bowl(2);
        h.NewBowler(b[2])
            .Bowl(1)
            .Bowl(1, d: new Dismissal(DismissalType.RunOut, a[2], b[1])).In(a[3])
            .Bowl(6, boundary: true)
            .Dots(3);

        h.Send(new InningsStarted(Guid.NewGuid(), h.TeamB, b[0], b[1], a[3]));
        h.Bowl(6, boundary: true)
            .Bowl(6, boundary: true)
            .Bowl(d: new Dismissal(DismissalType.Bowled, b[0])).In(b[2])
            .Bowl(4, boundary: true)
            .Bowl(extra: ExtraType.LegBye, extraRuns: 1)
            .Bowl(1);
        h.NewBowler(a[2])
            .Bowl(4, boundary: true)
            .Bowl(1);
        return h;
    }

    // Checks replaying the events gives the same state as applying them one by one.
    [Fact]
    public void ReplayEqualsStepByStep()
    {
        var h = ScriptedMatch();
        var replayed = MatchEngine.Replay(h.Setup, h.Events);
        Assert.Equal(Snapshot(h.State), Snapshot(replayed));
        Assert.Equal(Snapshot(h.State with { Innings = [h.State.Innings[0]] }),
            Snapshot(replayed with { Innings = [replayed.Innings[0]] }));
    }

    // Checks undo restores the exact state from before an over end, a wicket and an innings end.
    [Fact]
    public void UndoAcrossOverEndWicketAndInningsEnd()
    {
        var overEnd = MatchHarness.Create().Start().Dots(5);
        var before = Snapshot(overEnd.State);
        overEnd.Bowl(1).Undo();
        Assert.Equal(before, Snapshot(overEnd.State));

        var wicket = MatchHarness.Create().Start().Dots(5);
        before = Snapshot(wicket.State);
        wicket.Bowl(d: new Dismissal(DismissalType.Bowled, wicket.A[0])).Undo();
        Assert.Equal(before, Snapshot(wicket.State));

        var inningsEnd = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 1)).Start().Dots(5);
        before = Snapshot(inningsEnd.State);
        inningsEnd.Bowl(4, boundary: true);
        Assert.Equal(InningsEndReason.OversComplete, inningsEnd.State.Innings[0].EndReason);
        inningsEnd.Undo();
        Assert.Equal(before, Snapshot(inningsEnd.State));
        Assert.Equal(NextAction.Bowl, inningsEnd.State.Next);
    }

    // Checks run rate, required rate, runs needed and balls left.
    [Fact]
    public void RunRateAndRequiredRate()
    {
        var h = MatchHarness.Create().Start();
        for (var over = 0; over < 5; over++)
        {
            if (over > 0) h.NewBowler(h.B[over % 2 == 0 ? 10 : 9]);
            h.Bowl(4, boundary: true).Bowl(4, boundary: true).Bowl(1).Dots(3);
        }
        Assert.Equal(45, h.Inn.Runs);
        Assert.Equal("5.0", h.Inn.Overs);
        Assert.Equal(9.00m, h.Inn.RunRate);
        Assert.Null(h.Inn.RequiredRunRate);

        var chase = MatchHarness.Create().Start();
        chase.Send(new PenaltyRuns(Guid.NewGuid(), chase.TeamA, 150));
        chase.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        for (var over = 0; over < 5; over++)
        {
            if (over > 0) chase.NewBowler(chase.A[over % 2 == 0 ? 10 : 9]);
            chase.Bowl(4, boundary: true).Bowl(4, boundary: true).Bowl(1).Dots(3);
        }
        Assert.Equal(151, chase.Inn.Target);
        Assert.Equal(106, chase.Inn.RunsNeeded);
        Assert.Equal(90, chase.Inn.BallsLeft);
        Assert.Equal(7.07m, chase.Inn.RequiredRunRate);
    }

    // Checks run rate is zero before a ball is bowled.
    [Fact]
    public void RunRateZeroBeforeFirstBall() => Assert.Equal(0m, MatchHarness.Create().Start().Inn.RunRate);

    // Checks every figure on the scripted match's scorecard.
    [Fact]
    public void FullT20Scorecard()
    {
        var h = ScriptedMatch();
        var (a, b) = (h.A, h.B);
        var first = h.State.Innings[0];
        var second = h.State.Innings[1];

        Assert.Equal((22, 2, 12, InningsEndReason.OversComplete),
            (first.Runs, first.Wickets, first.LegalBalls, first.EndReason));
        Assert.Equal(new Extras(1, 1, 1, 0, 0), first.Extras);
        BatterLine(first, a[0], 8, 5, BatterStatus.Batting);
        BatterLine(first, a[1], 4, 2, BatterStatus.Out);
        BatterLine(first, a[2], 1, 2, BatterStatus.Out);
        BatterLine(first, a[3], 6, 4, BatterStatus.Batting);
        Assert.Equal(new BowlerFigures(b[3], 6, 0, 13, 0, 1, 1), first.Bowlers.Single(x => x.PlayerId == b[3]));
        Assert.Equal(new BowlerFigures(b[2], 6, 0, 8, 0, 0, 0), first.Bowlers.Single(x => x.PlayerId == b[2]));
        Assert.Equal([new FallOfWicket(1, 11, a[1], 3), new FallOfWicket(2, 16, a[2], 8)], first.FallOfWickets);
        Assert.Null(first.Batters.Single(x => x.PlayerId == a[1]).DismissedByBowlerId);

        Assert.Equal((23, 1, 8, InningsEndReason.TargetReached, 23),
            (second.Runs, second.Wickets, second.LegalBalls, second.EndReason, second.Target));
        Assert.Equal(new Extras(0, 0, 0, 1, 0), second.Extras);
        BatterLine(second, b[0], 12, 3, BatterStatus.Out);
        BatterLine(second, b[1], 6, 3, BatterStatus.Batting);
        BatterLine(second, b[2], 4, 2, BatterStatus.Batting);
        Assert.Equal(new BowlerFigures(a[3], 6, 0, 17, 1, 0, 0), second.Bowlers.Single(x => x.PlayerId == a[3]));
        Assert.Equal(new BowlerFigures(a[2], 2, 0, 5, 0, 0, 0), second.Bowlers.Single(x => x.PlayerId == a[2]));
        Assert.Equal([new FallOfWicket(1, 12, b[0], 3)], second.FallOfWickets);

        Assert.Equal(new MatchResult(ResultType.Win, h.TeamB, null, 2, null), h.State.Result);
        foreach (var inn in h.State.Innings)
            Assert.Equal(inn.Runs, inn.Batters.Sum(x => x.Runs) + inn.Extras.Total);
    }

    // Checks batter runs plus extras always equal the team total, after every single event.
    [Fact]
    public void TotalsAlwaysAddUp()
    {
        var h = ScriptedMatch();
        for (var count = 1; count <= h.Events.Count; count++)
        {
            var state = MatchEngine.Replay(h.Setup, h.Events.Take(count));
            foreach (var inn in state.Innings)
                Assert.Equal(inn.Runs, inn.Batters.Sum(x => x.Runs) + inn.Extras.Total);
        }
    }

    // Checks one batter's runs, balls faced and status.
    private static void BatterLine(InningsState inn, Guid id, int runs, int balls, BatterStatus status)
    {
        var line = inn.Batters.Single(x => x.PlayerId == id);
        Assert.Equal((runs, balls, status), (line.Runs, line.BallsFaced, line.Status));
    }
}
