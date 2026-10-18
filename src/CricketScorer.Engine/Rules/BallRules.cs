namespace CricketScorer.Engine;

public static class BallRules
{
    public const int MaxRunsOnOneBall = 7;

    // Total runs the batting team gets from this ball.
    public static int TeamRuns(BallBowled b) => b.Extra switch
    {
        ExtraType.Wide => 1 + b.ExtraRuns,
        ExtraType.NoBall => 1 + b.BatRuns + b.ExtraRuns,
        ExtraType.Bye or ExtraType.LegBye => b.ExtraRuns,
        _ => b.BatRuns
    };

    // Runs from this ball that count against the bowler.
    public static int RunsOffBowler(BallBowled b) => b.Extra switch
    {
        ExtraType.Wide => 1 + b.ExtraRuns,
        ExtraType.NoBall => 1 + b.BatRuns,
        ExtraType.Bye or ExtraType.LegBye => 0,
        _ => b.BatRuns
    };

    // Runs the batters actually ran, which decides whether they swap ends.
    public static int RunsRun(BallBowled b)
    {
        if (b.Boundary) return 0;
        return b.Extra switch
        {
            ExtraType.Wide => b.ExtraRuns,
            ExtraType.NoBall => b.BatRuns + b.ExtraRuns,
            ExtraType.Bye or ExtraType.LegBye => b.ExtraRuns,
            _ => b.BatRuns
        };
    }

    // Whether the ball counts towards the over.
    public static bool IsLegal(BallBowled b) => b.Extra is not (ExtraType.Wide or ExtraType.NoBall);

    // Whether the striker is charged with facing this ball.
    public static bool CountsAsBallFaced(BallBowled b) => b.Extra != ExtraType.Wide;

    // The short label shown for this ball in the current over, e.g. "4", "2wd", "nb+4", "1W".
    public static string ChipText(BallBowled b)
    {
        var text = b.Extra switch
        {
            ExtraType.Wide => b.ExtraRuns == 0 ? "wd" : $"{1 + b.ExtraRuns}wd",
            ExtraType.NoBall => b.BatRuns + b.ExtraRuns == 0 ? "nb" : $"nb+{b.BatRuns + b.ExtraRuns}",
            ExtraType.Bye => $"{b.ExtraRuns}b",
            ExtraType.LegBye => $"{b.ExtraRuns}lb",
            _ => b.BatRuns.ToString()
        };
        if (b.Dismissal is null) return text;
        return text == "0" ? "W" : text + "W";
    }

    // Returns why the run values on a ball don't make sense, or null if they do.
    public static string? ValidateRuns(BallBowled b)
    {
        if (b.BatRuns is < 0 or > MaxRunsOnOneBall || b.ExtraRuns is < 0 or > MaxRunsOnOneBall)
            return $"Runs on one ball must be between 0 and {MaxRunsOnOneBall}.";
        if (b.Extra is ExtraType.Wide or ExtraType.Bye or ExtraType.LegBye && b.BatRuns != 0)
            return "Runs can't be scored off the bat on a wide, bye or leg bye.";
        if (b.Extra is ExtraType.None && b.ExtraRuns != 0)
            return "Extra runs need an extra type.";
        if (b.Extra is ExtraType.Bye or ExtraType.LegBye && b.ExtraRuns == 0 && b.Dismissal is null)
            return "A bye or leg bye needs at least one run.";
        if (b.Boundary)
        {
            var offBat = b.Extra is ExtraType.None or ExtraType.NoBall;
            if (offBat ? b.BatRuns is not (4 or 6) || b.ExtraRuns != 0 : b.ExtraRuns != 4)
                return "A boundary is 4 or 6 off the bat, or 4 extras.";
        }
        return null;
    }
}
