namespace CricketScorer.Api.Infrastructure;

public static class StartupChecks
{
    public const int MinSigningKeyLength = 32;

    // Stops the app at startup if the connection string or signing key is missing from user-secrets.
    public static void RequireSecrets(IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
            throw Missing("ConnectionStrings:Default", "<connection string>");

        var key = config["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw Missing("Jwt:SigningKey", "<random key of at least 32 characters>");
        if (key.Length < MinSigningKeyLength)
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinSigningKeyLength} characters long.");
    }

    // Builds the error telling the developer which secret to set and how.
    private static InvalidOperationException Missing(string key, string example) =>
        new($"{key} is not set. Run: dotnet user-secrets set \"{key}\" \"{example}\" --project src/CricketScorer.Api");
}
