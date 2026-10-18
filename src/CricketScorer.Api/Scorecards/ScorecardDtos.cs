using CricketScorer.Api.Data;

namespace CricketScorer.Api.Scorecards;

public sealed record ExtrasDto(int Wides, int NoBalls, int Byes, int LegByes, int Penalties, int Total);

public sealed record BatterLineDto(string Name, string HowOut, int Runs, int Balls, int Fours, int Sixes, decimal StrikeRate);

public sealed record BowlerLineDto(string Name, string Overs, int Maidens, int Runs, int Wickets, decimal Economy, int Wides, int NoBalls);

public sealed record FallOfWicketDto(int Wicket, int Score, string Name, string Overs);

public sealed record InningsCardDto(
    string BattingTeamName,
    int Runs,
    int Wickets,
    string Overs,
    decimal RunRate,
    int? Target,
    ExtrasDto Extras,
    List<BatterLineDto> Batting,
    List<string> DidNotBat,
    List<BowlerLineDto> Bowling,
    List<FallOfWicketDto> FallOfWickets,
    bool IsComplete);

public sealed record ScorecardDto(
    Guid MatchId,
    string TeamAName,
    string TeamBName,
    DateOnly Date,
    string? Venue,
    MatchStatus Status,
    string? ResultText,
    List<InningsCardDto> Innings,
    DateTimeOffset? LastUpdated);
