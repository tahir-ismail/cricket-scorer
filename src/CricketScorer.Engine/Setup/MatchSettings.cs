namespace CricketScorer.Engine;

public sealed record MatchSettings(
    MatchFormat Format,
    int? OversPerSide,
    int? MaxOversPerBowler,
    int BallsPerOver,
    bool FreeHitsEnabled,
    bool DeclarationsAllowed)
{
    // Returns the default settings for a match format.
    public static MatchSettings ForFormat(MatchFormat format, int? customOvers = null) => format switch
    {
        MatchFormat.T20 => Limited(format, 20),
        MatchFormat.FiftyOvers => Limited(format, 50),
        MatchFormat.CustomOvers when customOvers is > 0 => Limited(format, customOvers.Value),
        MatchFormat.CustomOvers => throw new ArgumentException("Custom overs needs a number of overs above 0.", nameof(customOvers)),
        MatchFormat.Timed => new MatchSettings(format, null, null, 6, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    // Works out the most overs one bowler may bowl: overs divided by five, rounded up.
    public static int BowlerLimitFor(int overs) => (overs + 4) / 5;

    // Builds limited-overs settings for the given number of overs.
    private static MatchSettings Limited(MatchFormat format, int overs) =>
        new(format, overs, BowlerLimitFor(overs), 6, true, false);
}
