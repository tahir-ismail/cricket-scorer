namespace CricketScorer.Engine;

public enum ResultType
{
    Win,
    Tie,
    Draw,
    NoResult
}

public sealed record MatchResult(ResultType Type, Guid? WinnerTeamId, int? ByRuns, int? ByWickets, string? OverrideText);
