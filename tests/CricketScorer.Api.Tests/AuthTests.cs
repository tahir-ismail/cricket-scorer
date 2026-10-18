using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CricketScorer.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class AuthTests(ApiFactory factory)
{
    // Sends a register request with the given details.
    private Task<HttpResponseMessage> Register(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, "Tahir"), TestApi.Json);

    // Sends a login request with the given details.
    private Task<HttpResponseMessage> Login(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), TestApi.Json);

    // Makes a random email address for a test user.
    private static string NewEmail() => $"{Guid.NewGuid():N}@test.local";

    // Checks a new scorer can register and gets a working token.
    [Fact]
    public async Task RegisterReturnsTokenThatWorksOnMe()
    {
        var scorer = await factory.RegisterAsync("Tahir");
        var me = await (await scorer.Client.GetAsync("/api/auth/me")).ReadAsync<UserDto>();
        Assert.Equal(scorer.Email, me.Email);
        Assert.Equal("Tahir", me.DisplayName);
        Assert.Null(me.SchoolId);
    }

    // Checks registering the same email twice is refused.
    [Fact]
    public async Task DuplicateEmailIsRejected()
    {
        var email = NewEmail();
        (await Register(email, TestApi.Password)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(email, TestApi.Password)).StatusCode);
    }

    // Checks a 7-character password is refused and a plain 8-character one is allowed.
    [Fact]
    public async Task PasswordNeedsEightCharactersOnly()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(NewEmail(), "short12")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Register(NewEmail(), "abcdefgh")).StatusCode);
    }

    // Checks logging in with the right password returns a token valid for about 7 days.
    [Fact]
    public async Task LoginReturnsSevenDayToken()
    {
        var scorer = await factory.RegisterAsync();
        var response = await Login(scorer.Email, TestApi.Password);
        var auth = await response.ReadAsync<AuthResponse>();
        Assert.False(string.IsNullOrEmpty(auth.Token));
        Assert.InRange(auth.ExpiresAt, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));
    }

    // Checks a wrong password and an unknown email give the same 401 message.
    [Fact]
    public async Task WrongPasswordAndUnknownEmailLookTheSame()
    {
        var scorer = await factory.RegisterAsync();
        var wrongPassword = await Login(scorer.Email, "not-the-password");
        var unknownEmail = await Login(NewEmail(), TestApi.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var a = await wrongPassword.ReadAsync<ProblemDetails>();
        var b = await unknownEmail.ReadAsync<ProblemDetails>();
        Assert.Equal("Email or password is wrong.", a.Title);
        Assert.Equal(a.Title, b.Title);
    }

    // Checks endpoints need a token unless marked anonymous.
    [Fact]
    public async Task MeWithoutTokenIs401()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Checks a token signed with a different key is refused.
    [Fact]
    public async Task TokenWithWrongKeyIs401()
    {
        var scorer = await factory.RegisterAsync();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 64)));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", scorer.UserId.ToString())]),
            Issuer = "CricketScorer",
            Audience = "CricketScorer",
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
