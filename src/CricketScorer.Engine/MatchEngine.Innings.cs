namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Ends the innings automatically if nobody is left to bat.
    private static InningsState CheckInningsEnd(InningsState inn)
    {
        if (inn.IsComplete) return inn;
        var endEmpty = inn.StrikerId is null || inn.NonStrikerId is null;
        if (endEmpty && !inn.AvailableBatters.Any() && !inn.RetiredHurtBatters.Any())
            return inn with { EndReason = InningsEndReason.AllOut };
        return inn;
    }

    // Checks the scorer can end the innings now for this reason.
    private static string? ValidateInningsEnded(MatchState state, InningsEnded e)
    {
        if (state.Current is null) return "There's no innings in progress.";
        return e.Reason switch
        {
            InningsEndReason.AllOut when state.Next == NextAction.RetiredHurtDecision => null,
            InningsEndReason.AllOut => "All out is only chosen when the retired-hurt batters won't return.",
            _ => "Not supported yet."
        };
    }

    // Ends the current innings for the given reason.
    private static MatchState ApplyInningsEnded(MatchState state, InningsEnded e) =>
        ReplaceCurrent(state, state.Current! with { EndReason = e.Reason });
}
