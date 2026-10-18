using System.Net;
using System.Net.Http.Json;
using CricketScorer.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class StartupTests(ApiFactory factory)
{
    // Builds a configuration from the given key/value pairs.
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // Checks the API starts and answers the health check.
    [Fact]
    public async Task HealthCheckReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Checks a missing signing key stops startup with a helpful message.
    [Fact]
    public void MissingSigningKeyFailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupChecks.RequireSecrets(Config(("ConnectionStrings:Default", "x"))));
        Assert.Contains("Jwt:SigningKey", ex.Message);
        Assert.Contains("dotnet user-secrets set", ex.Message);
    }

    // Checks a signing key shorter than 32 characters is refused.
    [Fact]
    public void ShortSigningKeyFailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupChecks.RequireSecrets(Config(("ConnectionStrings:Default", "x"), ("Jwt:SigningKey", "short"))));
        Assert.Contains("32", ex.Message);
    }

    // Checks a missing connection string stops startup.
    [Fact]
    public void MissingConnectionStringFailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupChecks.RequireSecrets(Config(("Jwt:SigningKey", new string('k', 40)))));
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    // Checks an ApiException becomes a ProblemDetails response with its status code.
    [Fact]
    public async Task ApiExceptionBecomesProblemDetails()
    {
        var response = await factory.CreateClient().GetAsync("/test/throw");
        Assert.Equal((HttpStatusCode)418, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("teapot", problem?.Title);
    }
}
