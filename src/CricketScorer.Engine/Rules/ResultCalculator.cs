namespace CricketScorer.Engine;

public static class ResultCalculator
{
    // Works out the match result from the first two innings, or null if the scorer has to decide.
    public static MatchResult? FromFirstInnings(MatchSetup setup, InningsState first, InningsState second)
    {
        if (second.Runs > first.Runs)
        {
            var xiSize = setup.LineupFor(second.BattingTeamId)!.Players.Count;
            return new MatchResult(ResultType.Win, second.BattingTeamId, null, xiSize - 1 - second.Wickets, null);
        }

        var limitedOvers = second.OversLimit is not null;
        var finished = second.EndReason == InningsEndReason.AllOut
            || (limitedOvers && second.EndReason == InningsEndReason.OversComplete);
        if (finished)
        {
            return second.Runs == first.Runs
                ? new MatchResult(ResultType.Tie, null, null, null, null)
                : new MatchResult(ResultType.Win, first.BattingTeamId, first.Runs - second.Runs, null, null);
        }

        return limitedOvers ? null : new MatchResult(ResultType.Draw, null, null, null, null);
    }
}
