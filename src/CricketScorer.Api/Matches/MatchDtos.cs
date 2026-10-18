using CricketScorer.Api.Data;
using CricketScorer.Api.Teams;
using CricketScorer.Engine;

namespace CricketScorer.Api.Matches;

public sealed record CreateMatchRequest(
    Guid Id,
    DateOnly Date,
    string? Venue,
    Guid TeamAId,
    Guid TeamBId,
    List<Guid> TeamAPlayers,
    List<Guid> TeamBPlayers,
    Guid? TossWinnerTeamId,
    TossDecision? TossDecision,
    MatchFormat Format,
    int? OversPerSide,
    int? MaxOversPerBowler,
    int? BallsPerOver,
    bool? FreeHitsEnabled,
    string? DeviceLabel);

public sealed record MatchCreatedDto(Guid Id, string ShareCode, Guid SessionId);

public sealed record MatchSummaryDto(
    Guid Id,
    DateOnly Date,
    string? Venue,
    string TeamAName,
    string TeamBName,
    MatchStatus Status,
    string OwnerName,
    bool IsOwner);

public sealed record LineupDto(Guid TeamId, string Name, List<PlayerDto> Players);

public sealed record EventDto(int Sequence, MatchEvent Event);

public sealed record MatchDetailDto(
    Guid Id,
    string ShareCode,
    DateOnly Date,
    string? Venue,
    LineupDto TeamA,
    LineupDto TeamB,
    Guid? TossWinnerTeamId,
    TossDecision? TossDecision,
    MatchSettings Settings,
    MatchStatus Status,
    Guid OwnerUserId,
    string OwnerName,
    bool IsOwner,
    Guid? ActiveSessionId,
    List<EventDto> Events);

public sealed record StartSessionRequest(string? DeviceLabel);

public sealed record SessionStartedDto(Guid SessionId, List<EventDto> Events);
