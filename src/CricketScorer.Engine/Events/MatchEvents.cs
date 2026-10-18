using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CricketScorer.Engine;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(InningsStarted), "inningsStarted")]
[JsonDerivedType(typeof(BallBowled), "ball")]
[JsonDerivedType(typeof(BowlerChanged), "bowlerChanged")]
[JsonDerivedType(typeof(BatterIn), "batterIn")]
[JsonDerivedType(typeof(StrikeSwapped), "strikeSwapped")]
[JsonDerivedType(typeof(BatterRetired), "batterRetired")]
[JsonDerivedType(typeof(PenaltyRuns), "penaltyRuns")]
[JsonDerivedType(typeof(OversChanged), "oversChanged")]
[JsonDerivedType(typeof(TargetSet), "targetSet")]
[JsonDerivedType(typeof(InningsEnded), "inningsEnded")]
[JsonDerivedType(typeof(ResultOverridden), "resultOverridden")]
[JsonDerivedType(typeof(MatchClosed), "matchClosed")]
public abstract record MatchEvent(Guid Id);

public sealed record InningsStarted(Guid Id, Guid BattingTeamId, Guid StrikerId, Guid NonStrikerId, Guid BowlerId,
    ImmutableList<Guid>? BattingOrder = null) : MatchEvent(Id);

public sealed record BallBowled(Guid Id, int BatRuns = 0, ExtraType Extra = ExtraType.None, int ExtraRuns = 0,
    bool Boundary = false, Dismissal? Dismissal = null) : MatchEvent(Id);

public sealed record BowlerChanged(Guid Id, Guid BowlerId) : MatchEvent(Id);

public sealed record BatterIn(Guid Id, Guid BatterId) : MatchEvent(Id);

public sealed record StrikeSwapped(Guid Id) : MatchEvent(Id);

public sealed record BatterRetired(Guid Id, Guid BatterId, bool Hurt) : MatchEvent(Id);

public sealed record PenaltyRuns(Guid Id, Guid TeamId, int Runs = 5) : MatchEvent(Id);

public sealed record OversChanged(Guid Id, int OversPerSide) : MatchEvent(Id);

public sealed record TargetSet(Guid Id, int Target) : MatchEvent(Id);

public sealed record InningsEnded(Guid Id, InningsEndReason Reason) : MatchEvent(Id);

public sealed record ResultOverridden(Guid Id, ResultType Type, Guid? WinnerTeamId, string Text) : MatchEvent(Id);

public sealed record MatchClosed(Guid Id) : MatchEvent(Id);
