namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Ends the innings automatically when the target is reached, nobody is left to bat, or the overs run out.
    private static InningsState CheckInningsEnd(InningsState inn, int ballsPerOver)
    {
        if (inn.IsComplete) return inn;
        if (inn.Target is int target && inn.Runs >= target)
            return inn with { EndReason = InningsEndReason.TargetReached };
        var endEmpty = inn.StrikerId is null || inn.NonStrikerId is null;
        if (endEmpty && !inn.AvailableBatters.Any() && !inn.RetiredHurtBatters.Any())
            return inn with { EndReason = InningsEndReason.AllOut };
        if (inn.OversLimit is int overs && inn.LegalBalls >= overs * ballsPerOver)
            return inn with { EndReason = InningsEndReason.OversComplete };
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
            InningsEndReason.Declared when state.Setup.Settings.DeclarationsAllowed => null,
            InningsEndReason.Declared => "Declarations are only allowed in timed matches.",
            InningsEndReason.Manual => null,
            _ => "The app ends the innings by itself when the overs run out or the target is reached."
        };
    }

    // Ends the current innings for the given reason.
    private static MatchState ApplyInningsEnded(MatchState state, InningsEnded e) =>
        ReplaceCurrent(state, state.Current! with { EndReason = e.Reason });

    // Checks the overs per side can be changed now.
    private static string? ValidateOversChanged(MatchState state, OversChanged e)
    {
        if (state.Current is null) return "There's no innings in progress.";
        if (state.Current.OversLimit is null) return "A timed match has no overs limit to change.";
        if (e.OversPerSide < 1) return "Overs per side must be at least 1.";
        return null;
    }

    // Changes the overs for this innings (e.g. rain) and recalculates the bowler limit.
    private static MatchState ApplyOversChanged(MatchState state, OversChanged e)
    {
        var inn = state.Current! with
        {
            OversLimit = e.OversPerSide,
            MaxOversPerBowler = MatchSettings.BowlerLimitFor(e.OversPerSide)
        };
        return ReplaceCurrent(state, CheckInningsEnd(inn, state.Setup.Settings.BallsPerOver));
    }

    // Checks a target can be set now.
    private static string? ValidateTargetSet(MatchState state, TargetSet e)
    {
        if (state.Current is null) return "There's no innings in progress.";
        if (e.Target < 1) return "The target must be at least 1.";
        return null;
    }

    // Sets the target for the current innings.
    private static MatchState ApplyTargetSet(MatchState state, TargetSet e) =>
        ReplaceCurrent(state, CheckInningsEnd(state.Current! with { Target = e.Target }, state.Setup.Settings.BallsPerOver));

    // Checks penalty runs can be awarded now.
    private static string? ValidatePenaltyRuns(MatchState state, PenaltyRuns e)
    {
        if (state.Innings.Count == 0) return "The match hasn't started yet.";
        if (state.Setup.LineupFor(e.TeamId) is null) return "That team isn't playing in this match.";
        if (e.Runs < 1) return "Penalty runs must be at least 1.";
        return null;
    }

    // Gives penalty runs to a team: on their innings now, their last innings, or their next one.
    private static MatchState ApplyPenaltyRuns(MatchState state, PenaltyRuns e)
    {
        var index = state.Innings.FindLastIndex(i => i.BattingTeamId == e.TeamId);
        if (index < 0)
        {
            var pending = state.PendingPenalties.GetValueOrDefault(e.TeamId) + e.Runs;
            return state with { PendingPenalties = state.PendingPenalties.SetItem(e.TeamId, pending) };
        }

        var inn = AddPenalty(state.Innings[index], e.Runs);
        if (index == state.Innings.Count - 1) inn = CheckInningsEnd(inn, state.Setup.Settings.BallsPerOver);
        return state with { Innings = state.Innings.SetItem(index, inn) };
    }

    // Adds penalty runs to an innings total and its extras.
    private static InningsState AddPenalty(InningsState inn, int runs) =>
        inn with { Runs = inn.Runs + runs, Extras = inn.Extras with { Penalties = inn.Extras.Penalties + runs } };

    // Checks the batters can be swapped by hand now.
    private static string? ValidateStrikeSwapped(MatchState state) =>
        state.Next is NextAction.Bowl or NextAction.ChooseBowler ? null : $"Can't swap strike now: next step is {state.Next}.";

    // Swaps the batters by hand to fix the strike.
    private static MatchState ApplyStrikeSwapped(MatchState state) => ReplaceCurrent(state, SwapEnds(state.Current!));
}
