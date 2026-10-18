namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Checks a batter can come in now.
    private static string? ValidateBatterIn(MatchState state, BatterIn e)
    {
        if (state.Next is not (NextAction.ChooseBatter or NextAction.RetiredHurtDecision))
            return $"Can't send in a batter now: next step is {state.Next}.";
        var inn = state.Current!;
        if (!inn.AvailableBatters.Contains(e.BatterId) && !inn.RetiredHurtBatters.Contains(e.BatterId))
            return "That player can't bat now: they're not in the team, already out, or already batting.";
        return null;
    }

    // Puts a batter in at the empty end.
    private static MatchState ApplyBatterIn(MatchState state, BatterIn e)
    {
        var inn = state.Current!;
        inn = inn.Batters.Any(b => b.PlayerId == e.BatterId)
            ? inn.UpdateBatter(e.BatterId, b => b with { Status = BatterStatus.Batting })
            : inn with { Batters = inn.Batters.Add(BatterScore.New(e.BatterId)) };
        inn = inn.StrikerId is null ? inn with { StrikerId = e.BatterId } : inn with { NonStrikerId = e.BatterId };
        return ReplaceCurrent(state, inn);
    }

    // Checks a batter at the crease can retire now (between balls only).
    private static string? ValidateBatterRetired(MatchState state, BatterRetired e)
    {
        if (state.Next is not (NextAction.Bowl or NextAction.ChooseBowler))
            return $"Can't retire a batter now: next step is {state.Next}.";
        var inn = state.Current!;
        if (e.BatterId != inn.StrikerId && e.BatterId != inn.NonStrikerId)
            return "Only a batter at the crease can retire.";
        return null;
    }

    // Retires a batter: hurt isn't a wicket, retired out is.
    private static MatchState ApplyBatterRetired(MatchState state, BatterRetired e)
    {
        var inn = state.Current!.UpdateBatter(e.BatterId,
            b => b with { Status = e.Hurt ? BatterStatus.RetiredHurt : BatterStatus.RetiredOut });
        inn = e.Hurt ? ClearEnd(inn, e.BatterId) : TakeWicket(inn, e.BatterId);
        return ReplaceCurrent(state, CheckInningsEnd(inn, state.Setup.Settings.BallsPerOver));
    }
}
