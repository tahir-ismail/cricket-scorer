using System.Security.Cryptography;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CricketScorer.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; } =
        $@"Server=(localdb)\MSSQLLocalDB;Database=CricketScorerTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _signingKey = RandomNumberGenerator.GetString(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 64);

    // Points the API at a throwaway database and a random signing key, and adds a test-only failing endpoint.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", _signingKey);
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowEndpointFilter>());
    }

    // Creates the shared test database by running the real migrations.
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    // Drops the test database after all tests in the collection.
    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    private sealed class ThrowEndpointFilter : IStartupFilter
    {
        // Maps GET /test/throw, which throws an ApiException so the error handling can be tested.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(e => e.MapGet("/test/throw", (HttpContext _) => throw new ApiException(418, "teapot")).AllowAnonymous());
        };
    }
}
