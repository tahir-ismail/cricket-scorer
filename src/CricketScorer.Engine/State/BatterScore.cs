namespace CricketScorer.Engine;

public enum BatterStatus
{
    Batting,
    Out,
    RetiredHurt,
    RetiredOut
}

public sealed record BatterScore(
    Guid PlayerId,
    int Runs,
    int BallsFaced,
    int Fours,
    int Sixes,
    BatterStatus Status,
    Dismissal? Dismissal,
    Guid? DismissedByBowlerId)
{
    // Creates the line for a batter who has just come in.
    public static BatterScore New(Guid playerId) => new(playerId, 0, 0, 0, 0, BatterStatus.Batting, null, null);
}
