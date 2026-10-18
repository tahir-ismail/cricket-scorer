namespace CricketScorer.Engine.Tests;

public class ResultTests
{
    // Picks the next bowler in a five-man rotation when an over has ended.
    private static void NextBowlerIfNeeded(MatchHarness h)
    {
        if (h.State.Next != NextAction.ChooseBowler) return;
        var fielding = h.State.Setup.LineupFor(h.Inn.BowlingTeamId)!.Players;
        h.NewBowler(fielding[^(1 + h.Inn.LegalBalls / 6 % 5)]);
    }

    // Scores the given number of runs using sixes, fours, twos and singles.
    private static MatchHarness Score(MatchHarness h, int runs)
    {
        while (runs > 0 && h.State.Current is not null)
        {
            NextBowlerIfNeeded(h);
            var hit = runs >= 6 ? 6 : runs >= 4 ? 4 : runs >= 2 ? 2 : 1;
            h.Bowl(hit, boundary: hit is 4 or 6);
            runs -= hit;
        }
        return h;
    }

    // Takes the given number of wickets (bowled), sending in the next batter each time.
    private static MatchHarness Wickets(MatchHarness h, int count)
    {
        for (var i = 0; i < count && h.State.Current is not null; i++)
        {
            NextBowlerIfNeeded(h);
            h.Bowl(d: new Dismissal(DismissalType.Bowled, h.Inn.StrikerId!.Value));
            if (h.State.Next == NextAction.ChooseBatter) h.In(h.Inn.AvailableBatters.First());
        }
        return h;
    }

    // Ends the current innings by hand (or declares in a timed match).
    private static MatchHarness End(MatchHarness h, InningsEndReason reason = InningsEndReason.Manual) =>
        h.Send(new InningsEnded(Guid.NewGuid(), reason));

    // A 50-over harness, used so over limits don't get in the way.
    private static MatchHarness Fifty(int xi = 11) => MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.FiftyOvers), xi);

    // A timed-match harness.
    private static MatchHarness Timed() => MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.Timed));

    // Checks a successful chase three down is a seven-wicket win.
    [Fact]
    public void ChaseWonBySevenWickets()
    {
        var h = End(Score(Fifty().Start(), 150)).Start(teamABats: false);
        Wickets(h, 3);
        Score(h, 151);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamB, null, 7, null), h.State.Result);
    }

    // Checks the wicket margin uses the chasing side's XI size.
    [Fact]
    public void ChaseWonWithXiEightByFourWickets()
    {
        var h = End(Score(Fifty(8).Start(), 50)).Start(teamABats: false);
        Wickets(h, 3);
        Score(h, 51);
        Assert.Equal(4, h.State.Result!.ByWickets);
    }

    // Checks bowling the chasing side out short of the target is a win by runs.
    [Fact]
    public void DefendedAllOutWonByThirtyRuns()
    {
        var h = End(Score(Fifty().Start(), 150)).Start(teamABats: false);
        Score(h, 120);
        Assert.Null(h.State.Result);
        Wickets(h, 10);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamA, 30, null, null), h.State.Result);
    }

    // Checks falling one short when the overs run out is a one-run win.
    [Fact]
    public void OversCompleteWonByOneRun()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 2)).Start();
        for (var i = 0; i < 12; i++) { NextBowlerIfNeeded(h); h.Bowl(2); }
        h.Start(teamABats: false);
        for (var i = 0; i < 11; i++) { NextBowlerIfNeeded(h); h.Bowl(2); }
        h.Bowl(1);
        Assert.Equal(InningsEndReason.OversComplete, h.Inn.EndReason);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamA, 1, null, null), h.State.Result);
    }

    // Checks level scores when the overs run out is a tie.
    [Fact]
    public void LimitedLevelScoresTie()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 1)).Start();
        h.Bowl(2).Bowl(2).Bowl(2).Dots(3);
        h.Start(teamABats: false).Bowl(2).Bowl(2).Bowl(2).Dots(3);
        Assert.Equal(ResultType.Tie, h.State.Result!.Type);
        Assert.Null(h.State.Result.WinnerTeamId);
    }

    // Checks a timed match where the chasing side survives is a draw.
    [Fact]
    public void TimedNotAllOutIsDraw()
    {
        var h = End(Score(Timed().Start(), 150), InningsEndReason.Declared).Start(teamABats: false);
        Wickets(h, 6);
        End(Score(h, 140));
        Assert.Equal(ResultType.Draw, h.State.Result!.Type);
    }

    // Checks level scores in a timed match without being all out is still a draw.
    [Fact]
    public void TimedLevelNotAllOutIsDraw()
    {
        var h = End(Score(Timed().Start(), 150), InningsEndReason.Declared).Start(teamABats: false);
        End(Score(Wickets(h, 2), 150));
        Assert.Equal(ResultType.Draw, h.State.Result!.Type);
    }

    // Checks level scores in a timed match with the chasing side all out is a tie.
    [Fact]
    public void TimedLevelAllOutIsTie()
    {
        var h = End(Score(Timed().Start(), 150), InningsEndReason.Declared).Start(teamABats: false);
        Wickets(Score(h, 150), 10);
        Assert.Equal(ResultType.Tie, h.State.Result!.Type);
    }

    // Checks chasing down a declaration four down is a six-wicket win.
    [Fact]
    public void DeclaredThenChasedWonBySixWickets()
    {
        var h = End(Score(Timed().Start(), 200), InningsEndReason.Declared).Start(teamABats: false);
        Score(Wickets(h, 4), 201);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamB, null, 6, null), h.State.Result);
    }

    // Checks a rain-stopped limited-overs chase has no result until the scorer sets one.
    [Fact]
    public void RainEndedLimitedHasNoResultUntilOverridden()
    {
        var h = End(Score(Fifty().Start(), 150)).Start(teamABats: false);
        End(Score(Wickets(h, 2), 100));
        Assert.Null(h.State.Result);
        h.Send(new ResultOverridden(Guid.NewGuid(), ResultType.NoResult, null, "Rain stopped play"));
        Assert.Equal(new MatchResult(ResultType.NoResult, null, null, null, "Rain stopped play"), h.State.Result);
    }

    // Checks extra innings don't change the first-innings result.
    [Fact]
    public void ExtraInningsDoNotChangeResult()
    {
        var h = End(Score(Timed().Start(), 10), InningsEndReason.Declared).Start(teamABats: false);
        Score(h, 20);
        var result = h.State.Result;
        Assert.Equal(ResultType.Win, result!.Type);
        End(Score(h.Start(), 60), InningsEndReason.Declared).Start(teamABats: false);
        End(Score(h, 5));
        Assert.Equal(4, h.State.Innings.Count);
        Assert.Equal(result, h.State.Result);
    }

    // Checks a result override replaces the worked-out result and keeps its text.
    [Fact]
    public void OverrideReplacesResult()
    {
        var h = End(Score(Fifty().Start(), 50)).Start(teamABats: false);
        Assert.NotNull(h.Try(new ResultOverridden(Guid.NewGuid(), ResultType.Win, null, "x")));
        Assert.NotNull(h.Try(new ResultOverridden(Guid.NewGuid(), ResultType.Draw, h.TeamA, "x")));
        h.Send(new ResultOverridden(Guid.NewGuid(), ResultType.Win, h.TeamA, "Westville won on first innings"));
        Score(h, 51);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamA, null, null, "Westville won on first innings"), h.State.Result);
    }

    // Checks a result can't be set before the second innings starts.
    [Fact]
    public void OverrideNeedsSecondInnings()
    {
        var h = Fifty().Start();
        Assert.NotNull(h.Try(new ResultOverridden(Guid.NewGuid(), ResultType.Draw, null, "x")));
    }

    // Checks a closed match rejects every further event.
    [Fact]
    public void ClosedMatchRejectsEverything()
    {
        var h = Fifty().Start();
        Assert.NotNull(h.Try(new MatchClosed(Guid.NewGuid())));
        End(h).Send(new MatchClosed(Guid.NewGuid()));
        Assert.True(h.State.Closed);
        Assert.Equal(NextAction.MatchClosed, h.State.Next);
        Assert.NotNull(h.Try(new InningsStarted(Guid.NewGuid(), h.TeamB, h.B[0], h.B[1], h.A[0])));
        Assert.NotNull(h.Try(new PenaltyRuns(Guid.NewGuid(), h.TeamA)));
        Assert.NotNull(h.Try(new MatchClosed(Guid.NewGuid())));
    }
}
