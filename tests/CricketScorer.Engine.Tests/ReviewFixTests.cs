using System.Text.Json;

namespace CricketScorer.Engine.Tests;

public class ReviewFixTests
{
    // Checks penalty runs to the side that batted first raise the chase target.
    [Fact]
    public void PenaltyToFirstInningsSideMovesTarget()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        Assert.Equal(2, h.Inn.Target);
        h.Send(new PenaltyRuns(Guid.NewGuid(), h.TeamA));
        Assert.Equal(7, h.Inn.Target);
        h.Bowl(2);
        Assert.Null(h.Inn.EndReason);
    }

    // Checks a target set by hand isn't replaced by a later penalty.
    [Fact]
    public void PenaltyKeepsTargetSetByScorer()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        h.Send(new TargetSet(Guid.NewGuid(), 40));
        h.Send(new PenaltyRuns(Guid.NewGuid(), h.TeamA));
        Assert.Equal(40, h.Inn.Target);
    }

    // Checks reaching a revised (lower) target is a win.
    [Fact]
    public void RevisedTargetReachedIsWin()
    {
        var h = MatchHarness.Create().Start().Bowl(4).Bowl(4);
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        h.Send(new TargetSet(Guid.NewGuid(), 5));
        h.Bowl(6, boundary: true);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamB, null, 10, null), h.State.Result);
    }

    // Checks the runs margin is measured against a revised target.
    [Fact]
    public void RevisedTargetMissedMarginIsAgainstTarget()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 1)).Start();
        h.Bowl(6, boundary: true).Bowl(6, boundary: true).Bowl(6, boundary: true).Dots(3);
        h.Start(teamABats: false).Send(new TargetSet(Guid.NewGuid(), 10));
        h.Bowl(4, boundary: true).Dots(5);
        Assert.Equal(new MatchResult(ResultType.Win, h.TeamA, 5, null, null), h.State.Result);
    }

    // Checks one run short of a revised target with the overs done is a tie.
    [Fact]
    public void RevisedTargetMinusOneIsTie()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.CustomOvers, 1)).Start();
        h.Bowl(6, boundary: true).Bowl(6, boundary: true).Dots(4);
        h.Start(teamABats: false).Send(new TargetSet(Guid.NewGuid(), 5));
        h.Bowl(4, boundary: true).Dots(5);
        Assert.Equal(ResultType.Tie, h.State.Result!.Type);
    }

    // Checks only free-hit dismissals are allowed on a wide bowled on a free hit.
    [Fact]
    public void StumpedOnFreeHitWideRejected()
    {
        Assert.False(DismissalRules.IsAllowed(DismissalType.Stumped, ExtraType.Wide, freeHit: true));
        Assert.False(DismissalRules.IsAllowed(DismissalType.HitWicket, ExtraType.Wide, freeHit: true));
        Assert.True(DismissalRules.IsAllowed(DismissalType.RunOut, ExtraType.Wide, freeHit: true));
    }

    // Checks overs cut in the first innings carry into the second.
    [Fact]
    public void OversCutInFirstInningsCarryToSecond()
    {
        var h = MatchHarness.Create(MatchSettings.ForFormat(MatchFormat.FiftyOvers)).Start();
        h.Send(new OversChanged(Guid.NewGuid(), 30));
        h.Send(new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)).Start(teamABats: false);
        Assert.Equal((30, 6), (h.Inn.OversLimit, h.Inn.MaxOversPerBowler));
    }

    // Checks every kind of event survives a round trip through JSON as the base type.
    [Fact]
    public void EventsRoundTripThroughJson()
    {
        var id = Guid.NewGuid();
        MatchEvent[] events =
        [
            new InningsStarted(id, id, id, id, id, [id]),
            new BallBowled(id, 4, ExtraType.NoBall, 1, true, new Dismissal(DismissalType.RunOut, id, id)),
            new BowlerChanged(id, id),
            new BatterIn(id, id),
            new StrikeSwapped(id),
            new BatterRetired(id, id, true),
            new PenaltyRuns(id, id, 5),
            new OversChanged(id, 30),
            new TargetSet(id, 100),
            new InningsEnded(id, InningsEndReason.Declared),
            new ResultOverridden(id, ResultType.Win, id, "Won"),
            new MatchClosed(id)
        ];
        var json = JsonSerializer.Serialize(events);
        var back = JsonSerializer.Deserialize<MatchEvent[]>(json)!;
        Assert.Equal(events.Select(e => e.GetType()), back.Select(e => e.GetType()));
        Assert.Equal(json, JsonSerializer.Serialize(back));
        Assert.Equal(events[1], back[1]);
    }

    // Checks no runs can be scored on a ball where the striker is bowled, caught, LBW, stumped or hit wicket.
    [Theory]
    [InlineData(DismissalType.Bowled, 1, ExtraType.None, 0, false)]
    [InlineData(DismissalType.Caught, 4, ExtraType.None, 0, true)]
    [InlineData(DismissalType.Lbw, 0, ExtraType.LegBye, 1, false)]
    [InlineData(DismissalType.Stumped, 0, ExtraType.Wide, 1, false)]
    [InlineData(DismissalType.HitWicket, 2, ExtraType.None, 0, false)]
    public void NoRunsOnBowlerDismissals(DismissalType type, int bat, ExtraType extra, int extraRuns, bool boundary)
    {
        var h = MatchHarness.Create().Start();
        var fielder = DismissalRules.NeedsFielder(type) ? h.B[0] : (Guid?)null;
        Assert.NotNull(h.Try(new BallBowled(Guid.NewGuid(), bat, extra, extraRuns, boundary,
            new Dismissal(type, h.A[0], fielder))));
    }

    // Checks a stumping off a plain wide is still allowed.
    [Fact]
    public void StumpedOffPlainWideAllowed()
    {
        var h = MatchHarness.Create().Start();
        h.Bowl(extra: ExtraType.Wide, d: new Dismissal(DismissalType.Stumped, h.A[0], h.B[0]));
        Assert.Equal((1, 1), (h.Inn.Runs, h.Inn.Wickets));
    }
}
