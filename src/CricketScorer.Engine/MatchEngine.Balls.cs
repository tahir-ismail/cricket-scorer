namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Checks a ball can be bowled now and its values make sense.
    private static string? ValidateBall(MatchState state, BallBowled b)
    {
        if (state.Next != NextAction.Bowl) return $"Can't bowl now: next step is {state.Next}.";
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
            Extras = AddExtras(inn.Extras, b),
            ThisOver = inn.ThisOver.Add(BallRules.ChipText(b)),
            RunsThisOverOffBowler = inn.RunsThisOverOffBowler + offBowler,
            FreeHitNext = state.Setup.Settings.FreeHitsEnabled
                && (b.Extra == ExtraType.NoBall || (inn.FreeHitNext && b.Extra == ExtraType.Wide))
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
            LegalBalls = f.LegalBalls + (legal ? 1 : 0),
            Wides = f.Wides + (b.Extra == ExtraType.Wide ? 1 + b.ExtraRuns : 0),
            NoBalls = f.NoBalls + (b.Extra == ExtraType.NoBall ? 1 : 0)
        });

        if (BallRules.RunsRun(b) % 2 == 1) inn = SwapEnds(inn);

        if (legal && inn.LegalBalls % state.Setup.Settings.BallsPerOver == 0)
            inn = EndOver(inn, bowlerId);

        return ReplaceCurrent(state, inn);
    }

    // Adds this ball's extras to the innings breakdown (byes off a no-ball count as byes).
    private static Extras AddExtras(Extras x, BallBowled b) => b.Extra switch
    {
        ExtraType.Wide => x with { Wides = x.Wides + 1 + b.ExtraRuns },
        ExtraType.NoBall => x with { NoBalls = x.NoBalls + 1, Byes = x.Byes + b.ExtraRuns },
        ExtraType.Bye => x with { Byes = x.Byes + b.ExtraRuns },
        ExtraType.LegBye => x with { LegByes = x.LegByes + b.ExtraRuns },
        _ => x
    };

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
