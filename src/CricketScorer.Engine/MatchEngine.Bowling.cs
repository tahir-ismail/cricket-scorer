namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Checks a bowler can take over now.
    private static string? ValidateBowlerChanged(MatchState state, BowlerChanged e)
    {
        if (state.Next != NextAction.ChooseBowler) return $"Can't change bowler now: next step is {state.Next}.";
        if (!state.Setup.LineupFor(state.Current!.BowlingTeamId)!.Players.Contains(e.BowlerId))
            return "The bowler must be in the fielding team.";
        return null;
    }

    // Puts a new bowler on and starts a fresh over.
    private static MatchState ApplyBowlerChanged(MatchState state, BowlerChanged e)
    {
        var inn = state.Current!;
        inn = inn with { BowlerId = e.BowlerId, ThisOver = [], RunsThisOverOffBowler = 0 };
        if (inn.Bowlers.All(b => b.PlayerId != e.BowlerId))
            inn = inn.UpdateBowler(e.BowlerId, f => f);
        return ReplaceCurrent(state, inn);
    }
}
