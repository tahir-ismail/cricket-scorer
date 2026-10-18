namespace CricketScorer.Api.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName, Guid? SchoolId);

public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);
