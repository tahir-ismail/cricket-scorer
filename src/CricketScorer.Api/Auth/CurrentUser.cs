using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CricketScorer.Api.Auth;

public sealed class CurrentUser(IHttpContextAccessor http, AppDbContext db)
{
    private AppUser? _user;

    // The signed-in user's ID, read from the token's "sub" claim.
    public Guid UserId =>
        Guid.TryParse(http.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id
            : throw new ApiException(StatusCodes.Status401Unauthorized, "Sign in first.");

    // Loads the signed-in user from the database, so school changes apply straight away.
    public async Task<AppUser> GetAsync() =>
        _user ??= await db.Users.FindAsync(UserId)
                  ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Sign in first.");

    // Returns the user's school ID, or refuses with 403 if they haven't joined one.
    public async Task<Guid> RequireSchoolIdAsync() =>
        (await GetAsync()).SchoolId
        ?? throw new ApiException(StatusCodes.Status403Forbidden, "Join or create a school first.");
}
