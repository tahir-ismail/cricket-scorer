namespace CricketScorer.Api.Teams;

public sealed record CreateTeamRequest(Guid? Id, string Name, bool IsOpponent);

public sealed record UpdateTeamRequest(string Name, bool IsOpponent);

public sealed record CreatePlayerRequest(Guid? Id, string Name);

public sealed record UpdatePlayerRequest(string Name);

public sealed record PlayerDto(Guid Id, string Name);

public sealed record TeamSummaryDto(Guid Id, string Name, bool IsOpponent, int PlayerCount);

public sealed record TeamDto(Guid Id, string Name, bool IsOpponent, List<PlayerDto> Players);
