namespace CricketScorer.Engine.Tests;

public class SettingsTests
{
    // Builds a lineup of fresh player IDs for a new team.
    private static TeamLineup Lineup(int size) =>
        new(Guid.NewGuid(), Enumerable.Range(0, size).Select(_ => Guid.NewGuid()).ToImmutableList());

    // Checks the T20 format defaults.
    [Fact]
    public void T20Defaults()
    {
        var s = MatchSettings.ForFormat(MatchFormat.T20);
        Assert.Equal((20, 4, 6, true, false),
            (s.OversPerSide, s.MaxOversPerBowler, s.BallsPerOver, s.FreeHitsEnabled, s.DeclarationsAllowed));
    }

    // Checks the 50-over format defaults.
    [Fact]
    public void FiftyOverDefaults()
    {
        var s = MatchSettings.ForFormat(MatchFormat.FiftyOvers);
        Assert.Equal((50, 10, 6, true, false),
            (s.OversPerSide, s.MaxOversPerBowler, s.BallsPerOver, s.FreeHitsEnabled, s.DeclarationsAllowed));
    }

    // Checks the custom bowler limit is overs divided by five, rounded up.
    [Theory]
    [InlineData(30, 6)]
    [InlineData(40, 8)]
    [InlineData(1, 1)]
    [InlineData(12, 3)]
    public void CustomOversBowlerLimitIsOversDividedByFiveRoundedUp(int overs, int max)
    {
        var s = MatchSettings.ForFormat(MatchFormat.CustomOvers, overs);
        Assert.Equal(overs, s.OversPerSide);
        Assert.Equal(max, s.MaxOversPerBowler);
    }

    // Checks a custom format needs an overs count.
    [Fact]
    public void CustomOversWithoutCountThrows() =>
        Assert.Throws<ArgumentException>(() => MatchSettings.ForFormat(MatchFormat.CustomOvers));

    // Checks the timed format defaults.
    [Fact]
    public void TimedDefaults()
    {
        var s = MatchSettings.ForFormat(MatchFormat.Timed);
        Assert.Equal(((int?)null, (int?)null, 6, false, true),
            (s.OversPerSide, s.MaxOversPerBowler, s.BallsPerOver, s.FreeHitsEnabled, s.DeclarationsAllowed));
    }

    // Checks the XI must have between 2 and 15 players.
    [Fact]
    public void SetupRejectsXiSmallerThanTwoOrLargerThanFifteen()
    {
        var t20 = MatchSettings.ForFormat(MatchFormat.T20);
        Assert.NotNull(new MatchSetup(t20, Lineup(1), Lineup(11)).Validate());
        Assert.NotNull(new MatchSetup(t20, Lineup(11), Lineup(16)).Validate());
        Assert.Null(new MatchSetup(t20, Lineup(2), Lineup(15)).Validate());
    }

    // Checks duplicate players, the same team twice and shared players are rejected.
    [Fact]
    public void SetupRejectsDuplicatePlayersAndSameTeamTwiceAndPlayerOnBothSides()
    {
        var t20 = MatchSettings.ForFormat(MatchFormat.T20);
        var a = Lineup(11);
        var dup = a with { Players = a.Players.SetItem(1, a.Players[0]) };
        Assert.NotNull(new MatchSetup(t20, dup, Lineup(11)).Validate());

        var b = Lineup(11);
        Assert.NotNull(new MatchSetup(t20, a, b with { TeamId = a.TeamId }).Validate());
        Assert.NotNull(new MatchSetup(t20, a, b with { Players = b.Players.SetItem(0, a.Players[0]) }).Validate());
    }

    // Checks a normal setup passes validation.
    [Fact]
    public void ValidSetupReturnsNull() =>
        Assert.Null(new MatchSetup(MatchSettings.ForFormat(MatchFormat.T20), Lineup(11), Lineup(11)).Validate());
}
