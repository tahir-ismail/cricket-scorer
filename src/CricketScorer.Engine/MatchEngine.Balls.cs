namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Checks a ball can be bowled now and its values make sense.
    private static string? ValidateBall(MatchState state, BallBowled b)
    {
        if (state.Next != NextAction.Bowl) return $"Can't bowl now: next step is {state.Next}.";
        if (b.Extra != ExtraType.None) return "Not supported yet.";
        return BallRules.ValidateRuns(b);
    }

    // Applies a ball: runs, batter and bowler figures, strike rotation and the end of the over.
    private static MatchState ApplyBall(MatchState state, BallBowled b)
    {
        var inn = state.Current!;
        var strikerId = inn.StrikerId!.Value;
        var bowlerId = inn.BowlerId!.Value;
        var legal = BallRules.IsLegal(b);
        var offBowler = BallRules.RunsOffBowler(b);
        var batCredited = b.Extra is ExtraType.None or ExtraType.NoBall ? b.BatRuns : 0;

        inn = inn with
        {
            Runs = inn.Runs + BallRules.TeamRuns(b),
            LegalBalls = inn.LegalBalls + (legal ? 1 : 0),
            ThisOver = inn.ThisOver.Add(BallRules.ChipText(b)),
            RunsThisOverOffBowler = inn.RunsThisOverOffBowler + offBowler
        };

        inn = inn.UpdateBatter(strikerId, s => s with
        {
            Runs = s.Runs + batCredited,
            BallsFaced = s.BallsFaced + (BallRules.CountsAsBallFaced(b) ? 1 : 0),
            Fours = s.Fours + (b.Boundary && batCredited == 4 ? 1 : 0),
            Sixes = s.Sixes + (b.Boundary && batCredited == 6 ? 1 : 0)
        });

        inn = inn.UpdateBowler(bowlerId, f => f with
        {
            RunsConceded = f.RunsConceded + offBowler,
            LegalBalls = f.LegalBalls + (legal ? 1 : 0)
        });

        if (BallRules.RunsRun(b) % 2 == 1) inn = SwapEnds(inn);

        if (legal && inn.LegalBalls % state.Setup.Settings.BallsPerOver == 0)
            inn = EndOver(inn, bowlerId);

        return ReplaceCurrent(state, inn);
    }

    // Finishes an over: records a maiden, swaps ends and waits for the next bowler.
    private static InningsState EndOver(InningsState inn, Guid bowlerId)
    {
        if (inn.RunsThisOverOffBowler == 0)
            inn = inn.UpdateBowler(bowlerId, f => f with { Maidens = f.Maidens + 1 });
        return SwapEnds(inn) with { PreviousOverBowlerId = bowlerId, BowlerId = null };
    }

    // Swaps the striker and non-striker.
    private static InningsState SwapEnds(InningsState inn) =>
        inn with { StrikerId = inn.NonStrikerId, NonStrikerId = inn.StrikerId };

    // Puts the updated current innings back into the match state.
    private static MatchState ReplaceCurrent(MatchState state, InningsState inn) =>
        state with { Innings = state.Innings.SetItem(state.Innings.Count - 1, inn) };
}
