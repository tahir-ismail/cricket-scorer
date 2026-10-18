namespace CricketScorer.Api.Schools;

public sealed record CreateSchoolRequest(string Name);

public sealed record JoinSchoolRequest(string JoinCode);

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, bool IsAdmin);

public sealed record SchoolDto(Guid Id, string Name, bool IsAdmin, string? JoinCode, List<MemberDto> Members);

public sealed record JoinCodeDto(string JoinCode);
