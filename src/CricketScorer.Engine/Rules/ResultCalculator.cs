namespace CricketScorer.Engine;

public static class ResultCalculator
{
    // Works out the match result from the first two innings, or null if the scorer has to decide.
    public static MatchResult? FromFirstInnings(MatchSetup setup, InningsState first, InningsState second)
    {
        var target = second.Target ?? first.Runs + 1;
        if (second.Runs >= target)
        {
            var xiSize = setup.LineupFor(second.BattingTeamId)!.Players.Count;
            return new MatchResult(ResultType.Win, second.BattingTeamId, null, xiSize - 1 - second.Wickets, null);
        }

        var limitedOvers = second.OversLimit is not null;
        var finished = second.EndReason == InningsEndReason.AllOut
            || (limitedOvers && second.EndReason == InningsEndReason.OversComplete);
        if (finished)
        {
            var margin = target - 1 - second.Runs;
            return margin == 0
                ? new MatchResult(ResultType.Tie, null, null, null, null)
                : new MatchResult(ResultType.Win, first.BattingTeamId, margin, null, null);
        }

        return limitedOvers ? null : new MatchResult(ResultType.Draw, null, null, null, null);
    }
}
