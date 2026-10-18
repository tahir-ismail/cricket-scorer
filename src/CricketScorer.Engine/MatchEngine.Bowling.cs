namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Checks a bowler can take over now, at the start of an over or mid-over.
    private static string? ValidateBowlerChanged(MatchState state, BowlerChanged e)
    {
        if (state.Next is not (NextAction.ChooseBowler or NextAction.Bowl))
            return $"Can't change bowler now: next step is {state.Next}.";
        var inn = state.Current!;
        if (!state.Setup.LineupFor(inn.BowlingTeamId)!.Players.Contains(e.BowlerId))
            return "The bowler must be in the fielding team.";
        if (e.BowlerId == inn.BowlerId) return "That bowler is already bowling.";
        if (e.BowlerId == inn.PreviousOverBowlerId) return "A bowler can't bowl two overs in a row.";
        if (inn.MaxOversPerBowler is int max && OversStarted(inn, e.BowlerId, state.Setup.Settings.BallsPerOver) >= max)
            return $"That bowler has already bowled their {max} overs.";
        return null;
    }

    // Puts a new bowler on: a fresh over if the last one ended, otherwise they finish this one.
    private static MatchState ApplyBowlerChanged(MatchState state, BowlerChanged e)
    {
        var inn = state.Current!;
        inn = inn.BowlerId is null
            ? inn with { BowlerId = e.BowlerId, ThisOver = [], RunsThisOverOffBowler = 0, OverShared = false }
            : inn with { BowlerId = e.BowlerId, OverShared = true };
        if (inn.Bowlers.All(b => b.PlayerId != e.BowlerId))
            inn = inn.UpdateBowler(e.BowlerId, f => f);
        return ReplaceCurrent(state, inn);
    }

    // Counts the overs a bowler has bowled, with a part-over counting as a whole one.
    private static int OversStarted(InningsState inn, Guid bowlerId, int ballsPerOver)
    {
        var balls = inn.Bowlers.FirstOrDefault(b => b.PlayerId == bowlerId)?.LegalBalls ?? 0;
        return (balls + ballsPerOver - 1) / ballsPerOver;
    }
}
