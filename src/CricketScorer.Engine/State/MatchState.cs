using System.Collections.Immutable;

namespace CricketScorer.Engine;

public enum NextAction
{
    StartInnings,
    Bowl,
    ChooseBowler,
    ChooseBatter,
    RetiredHurtDecision,
    MatchClosed
}

public sealed record MatchState(
    MatchSetup Setup,
    ImmutableList<InningsState> Innings,
    MatchResult? Result,
    bool Closed,
    ImmutableDictionary<Guid, int> PendingPenalties)
{
    // Creates the state of a match before anything has happened.
    public static MatchState Initial(MatchSetup setup) =>
        new(setup, ImmutableList<InningsState>.Empty, null, false, ImmutableDictionary<Guid, int>.Empty);

    // The innings being played right now, or null between innings.
    public InningsState? Current => Innings.Count > 0 && !Innings[^1].IsComplete ? Innings[^1] : null;

    // What the scorer needs to do next.
    public NextAction Next
    {
        get
        {
            if (Closed) return NextAction.MatchClosed;
            var inn = Current;
            if (inn is null) return NextAction.StartInnings;
            if (inn.StrikerId is null || inn.NonStrikerId is null)
                return inn.AvailableBatters.Any() ? NextAction.ChooseBatter : NextAction.RetiredHurtDecision;
            if (inn.BowlerId is null) return NextAction.ChooseBowler;
            return NextAction.Bowl;
        }
    }
}
