namespace CricketScorer.Engine;

public sealed record BowlerFigures(
    Guid PlayerId,
    int LegalBalls,
    int Maidens,
    int RunsConceded,
    int Wickets,
    int Wides,
    int NoBalls)
{
    // Creates figures for a bowler who hasn't bowled yet.
    public static BowlerFigures Empty(Guid playerId) => new(playerId, 0, 0, 0, 0, 0, 0);
}
