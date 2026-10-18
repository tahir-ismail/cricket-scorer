namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Updates the result from the first two innings, unless the scorer has set one by hand.
    private static MatchState WithResult(MatchState state)
    {
        if (state.Result?.OverrideText is not null) return state;
        var result = state.Innings.Count >= 2 && state.Innings[1].IsComplete
            ? ResultCalculator.FromFirstInnings(state.Setup, state.Innings[0], state.Innings[1])
            : null;
        return state with { Result = result };
    }

    // Checks a result can be set by hand now.
    private static string? ValidateResultOverridden(MatchState state, ResultOverridden e)
    {
        if (state.Innings.Count < 2) return "A result can only be set once the second innings has started.";
        if (e.Type == ResultType.Win && (e.WinnerTeamId is null || state.Setup.LineupFor(e.WinnerTeamId.Value) is null))
            return "A win needs the winning team.";
        if (e.Type != ResultType.Win && e.WinnerTeamId is not null)
            return "Only a win has a winning team.";
        if (string.IsNullOrWhiteSpace(e.Text)) return "Describe the result.";
        return null;
    }

    // Sets the result by hand.
    private static MatchState ApplyResultOverridden(MatchState state, ResultOverridden e) =>
        state with { Result = new MatchResult(e.Type, e.WinnerTeamId, null, null, e.Text) };

    // Checks the match can be closed now.
    private static string? ValidateMatchClosed(MatchState state)
    {
        if (state.Innings.Count == 0) return "The match hasn't started yet.";
        if (state.Current is not null) return "End the current innings before closing the match.";
        return null;
    }

    // Closes the match so nothing more can be scored.
    private static MatchState ApplyMatchClosed(MatchState state) => state with { Closed = true };
}
