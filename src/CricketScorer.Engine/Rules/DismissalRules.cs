namespace CricketScorer.Engine;

public static class DismissalRules
{
    // Whether this kind of dismissal can happen on this kind of ball.
    public static bool IsAllowed(DismissalType type, ExtraType extra, bool freeHit)
    {
        if (type is DismissalType.RunOut or DismissalType.ObstructingField or DismissalType.Other) return true;
        return extra switch
        {
            ExtraType.None => !freeHit,
            ExtraType.Wide => !freeHit && type is DismissalType.Stumped or DismissalType.HitWicket,
            _ => false
        };
    }

    // Whether the bowler gets the wicket.
    public static bool CreditsBowler(DismissalType type) =>
        type is DismissalType.Bowled or DismissalType.Caught or DismissalType.Lbw
            or DismissalType.Stumped or DismissalType.HitWicket;

    // Whether the dismissal has to name a fielder.
    public static bool NeedsFielder(DismissalType type) => type is DismissalType.Caught or DismissalType.Stumped;

    // Whether the non-striker can be the batter out.
    public static bool EitherBatter(DismissalType type) =>
        type is DismissalType.RunOut or DismissalType.ObstructingField or DismissalType.Other;
}
