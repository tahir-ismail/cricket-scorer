namespace CricketScorer.Engine;

public sealed record Dismissal(DismissalType Type, Guid BatterOutId, Guid? FielderId = null);
