using System.Security.Claims;
using System.Text;
using CricketScorer.Api.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CricketScorer.Api.Auth;

public sealed class TokenService(IConfiguration config)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    // Builds the key that signs and checks tokens, from the Jwt:SigningKey secret.
    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!));

    // Creates a signed token for the user that lasts 7 days.
    public string Create(AppUser user, out DateTimeOffset expiresAt)
    {
        expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName)
            ]),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256)
        });
    }
}
