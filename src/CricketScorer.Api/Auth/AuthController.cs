using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CricketScorer.Api.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<AppUser> users, TokenService tokens, CurrentUser current) : ControllerBase
{
    private const string WrongLogin = "Email or password is wrong.";

    // Creates a scorer account and signs them in.
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = Names.Require(request.DisplayName, 80, "display")
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }
        return Signed(user);
    }

    // Checks the email and password and returns a token, locking the account after repeated failures.
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || await users.IsLockedOutAsync(user))
            throw new ApiException(StatusCodes.Status401Unauthorized, WrongLogin);

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new ApiException(StatusCodes.Status401Unauthorized, WrongLogin);
        }

        await users.ResetAccessFailedCountAsync(user);
        return Signed(user);
    }

    // Returns the signed-in scorer's details.
    [HttpGet("me")]
    public async Task<UserDto> Me() => ToDto(await current.GetAsync());

    // Builds the token response for a user.
    private AuthResponse Signed(AppUser user) =>
        new(tokens.Create(user, out var expiresAt), expiresAt, ToDto(user));

    // Converts a user to the shape the API returns.
    private static UserDto ToDto(AppUser user) => new(user.Id, user.Email!, user.DisplayName, user.SchoolId);
}
