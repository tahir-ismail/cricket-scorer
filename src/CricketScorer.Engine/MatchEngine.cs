using System.Collections.Immutable;

namespace CricketScorer.Engine;

public static partial class MatchEngine
{
    // Returns why an event isn't allowed right now, or null if it is.
    public static string? Validate(MatchState state, MatchEvent e)
    {
        if (state.Closed) return "The match is closed.";
        return e switch
        {
            InningsStarted s => ValidateInningsStarted(state, s),
            BallBowled b => ValidateBall(state, b),
            BowlerChanged c => ValidateBowlerChanged(state, c),
            BatterIn i => ValidateBatterIn(state, i),
            BatterRetired r => ValidateBatterRetired(state, r),
            InningsEnded x => ValidateInningsEnded(state, x),
            OversChanged o => ValidateOversChanged(state, o),
            TargetSet t => ValidateTargetSet(state, t),
            PenaltyRuns p => ValidatePenaltyRuns(state, p),
            StrikeSwapped => ValidateStrikeSwapped(state),
            _ => "Not supported yet."
        };
    }

    // Applies an event and returns the new state, throwing if the event isn't allowed.
    public static MatchState Apply(MatchState state, MatchEvent e)
    {
        var error = Validate(state, e);
        if (error is not null) throw new InvalidEventException(error);
        return e switch
        {
            InningsStarted s => ApplyInningsStarted(state, s),
            BallBowled b => ApplyBall(state, b),
            BowlerChanged c => ApplyBowlerChanged(state, c),
            BatterIn i => ApplyBatterIn(state, i),
            BatterRetired r => ApplyBatterRetired(state, r),
            InningsEnded x => ApplyInningsEnded(state, x),
            OversChanged o => ApplyOversChanged(state, o),
            TargetSet t => ApplyTargetSet(state, t),
            PenaltyRuns p => ApplyPenaltyRuns(state, p),
            StrikeSwapped => ApplyStrikeSwapped(state),
            _ => throw new InvalidEventException("Not supported yet.")
        };
    }

    // Rebuilds a match state by applying every event in order.
    public static MatchState Replay(MatchSetup setup, IEnumerable<MatchEvent> events) =>
        events.Aggregate(MatchState.Initial(setup), Apply);

    // Checks an innings can start with these batters and bowler.
    private static string? ValidateInningsStarted(MatchState state, InningsStarted e)
    {
        if (state.Current is not null) return "An innings is already in progress.";
        var batting = state.Setup.LineupFor(e.BattingTeamId);
        if (batting is null) return "That team isn't playing in this match.";
        if (state.Innings.Count == 1 && state.Innings[0].BattingTeamId == e.BattingTeamId)
            return "The other team bats in the second innings.";

        var order = e.BattingOrder ?? batting.Players;
        if (order.Count != batting.Players.Count || order.Except(batting.Players).Any())
            return "The batting order must list exactly the players in the XI.";
        if (e.StrikerId == e.NonStrikerId) return "The two opening batters must be different players.";
        if (!order.Contains(e.StrikerId) || !order.Contains(e.NonStrikerId))
            return "Both opening batters must be in the batting team.";
        if (!state.Setup.OpponentOf(e.BattingTeamId).Players.Contains(e.BowlerId))
            return "The bowler must be in the fielding team.";
        return null;
    }

    // Starts a new innings with its opening batters and bowler.
    private static MatchState ApplyInningsStarted(MatchState state, InningsStarted e)
    {
        var settings = state.Setup.Settings;
        var innings = new InningsState(
            BattingTeamId: e.BattingTeamId,
            BowlingTeamId: state.Setup.OpponentOf(e.BattingTeamId).TeamId,
            BattingOrder: e.BattingOrder ?? state.Setup.LineupFor(e.BattingTeamId)!.Players,
            Runs: 0,
            Wickets: 0,
            LegalBalls: 0,
            Extras: Extras.None,
            Batters: [BatterScore.New(e.StrikerId), BatterScore.New(e.NonStrikerId)],
            Bowlers: [BowlerFigures.Empty(e.BowlerId)],
            FallOfWickets: [],
            StrikerId: e.StrikerId,
            NonStrikerId: e.NonStrikerId,
            BowlerId: e.BowlerId,
            PreviousOverBowlerId: null,
            ThisOver: [],
            RunsThisOverOffBowler: 0,
            OverShared: false,
            FreeHitNext: false,
            OversLimit: settings.OversPerSide,
            MaxOversPerBowler: settings.MaxOversPerBowler,
            Target: state.Innings.Count == 1 ? state.Innings[0].Runs + 1 : null,
            EndReason: null);

        if (state.PendingPenalties.TryGetValue(e.BattingTeamId, out var penalty))
        {
            innings = AddPenalty(innings, penalty);
            state = state with { PendingPenalties = state.PendingPenalties.Remove(e.BattingTeamId) };
        }
        return state with { Innings = state.Innings.Add(innings) };
    }
}
