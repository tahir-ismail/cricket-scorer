namespace CricketScorer.Engine.Tests;

public class RetirementTests
{
    // Gets the striker out bowled and sends in the next available batter, if any.
    private static MatchHarness BowledAndNext(MatchHarness h)
    {
        h.Bowl(d: new Dismissal(DismissalType.Bowled, h.Inn.StrikerId!.Value));
        if (h.State.Next == NextAction.ChooseBatter) h.In(h.Inn.AvailableBatters.First());
        if (h.State.Next == NextAction.ChooseBowler)
            h.NewBowler(h.Inn.PreviousOverBowlerId == h.B[^1] ? h.B[^2] : h.B[^1]);
        return h;
    }

    // Checks retiring hurt isn't a wicket and asks for a new batter.
    [Fact]
    public void RetiredHurtIsNotAWicket()
    {
        var h = MatchHarness.Create().Start().Bowl(1);
        h.Send(new BatterRetired(Guid.NewGuid(), h.A[0], Hurt: true));
        Assert.Equal(0, h.Inn.Wickets);
        Assert.Empty(h.Inn.FallOfWickets);
        Assert.Equal(BatterStatus.RetiredHurt, h.Batter(h.A[0]).Status);
        Assert.Equal(NextAction.ChooseBatter, h.State.Next);
        h.In(h.A[2]);
        Assert.Equal(h.A[2], h.Inn.NonStrikerId);
    }

    // Checks a retired-hurt batter can come back later.
    [Fact]
    public void RetiredHurtCanReturn()
    {
        var h = MatchHarness.Create().Start().Bowl(3);
        h.Send(new BatterRetired(Guid.NewGuid(), h.A[0], Hurt: true)).In(h.A[2]);
        h.Bowl(d: new Dismissal(DismissalType.Bowled, h.A[1]));
        Assert.Null(h.Try(new BatterIn(Guid.NewGuid(), h.A[0])));
        h.In(h.A[0]);
        var b = h.Batter(h.A[0]);
        Assert.Equal((BatterStatus.Batting, 3), (b.Status, b.Runs));
    }

    // Checks retiring out counts as a wicket but not for the bowler.
    [Fact]
    public void RetiredOutCountsAsWicket()
    {
        var h = MatchHarness.Create().Start().Bowl(2);
        h.Send(new BatterRetired(Guid.NewGuid(), h.A[0], Hurt: false));
        Assert.Equal(1, h.Inn.Wickets);
        Assert.Equal(new FallOfWicket(1, 2, h.A[0], 1), h.Inn.FallOfWickets[0]);
        Assert.Equal(BatterStatus.RetiredOut, h.Batter(h.A[0]).Status);
        Assert.Equal(0, h.Bowler(h.B[10]).Wickets);
    }

    // Checks ten wickets with eleven players ends the innings.
    [Fact]
    public void AllOutWithElevenIsTenWickets()
    {
        var h = MatchHarness.Create().Start();
        for (var i = 0; i < 9; i++) BowledAndNext(h);
        Assert.Null(h.Inn.EndReason);
        h.Bowl(d: new Dismissal(DismissalType.Bowled, h.Inn.StrikerId!.Value));
        Assert.Equal(10, h.Inn.Wickets);
        Assert.Equal(InningsEndReason.AllOut, h.Inn.EndReason);
        Assert.Equal(NextAction.StartInnings, h.State.Next);
    }

    // Checks a six-player team is all out after five wickets.
    [Fact]
    public void AllOutWithSmallXi()
    {
        var h = MatchHarness.Create(xiSize: 6).Start();
        for (var i = 0; i < 5; i++) BowledAndNext(h);
        Assert.Equal(5, h.Inn.Wickets);
        Assert.Equal(InningsEndReason.AllOut, h.Inn.EndReason);
    }

    // Checks the scorer is asked whether a retired-hurt batter returns when nobody else is left.
    [Fact]
    public void LastBatterOutWithRetiredHurtAsksScorer()
    {
        var h = MatchHarness.Create(xiSize: 3).Start();
        h.Send(new BatterRetired(Guid.NewGuid(), h.A[0], Hurt: true)).In(h.A[2]);
        h.Bowl(d: new Dismissal(DismissalType.Bowled, h.A[2]));
        Assert.Equal(NextAction.RetiredHurtDecision, h.State.Next);
        Assert.Null(h.Inn.EndReason);

        var ended = MatchEngine.Apply(h.State, new InningsEnded(Guid.NewGuid(), InningsEndReason.AllOut));
        Assert.Equal(InningsEndReason.AllOut, ended.Innings[^1].EndReason);

        h.In(h.A[0]);
        Assert.Equal(NextAction.Bowl, h.State.Next);
    }

    // Checks only a batter at the crease can retire.
    [Fact]
    public void RetireRejectedForPlayerNotBatting()
    {
        var h = MatchHarness.Create().Start();
        Assert.NotNull(h.Try(new BatterRetired(Guid.NewGuid(), h.A[5], true)));
        Assert.NotNull(h.Try(new BatterRetired(Guid.NewGuid(), h.B[0], false)));
        Assert.NotNull(h.Try(new InningsEnded(Guid.NewGuid(), InningsEndReason.AllOut)));
    }
}
