namespace CricketScorer.Api.Infrastructure;

public static class Names
{
    // Trims a name and refuses it with 400 if it's blank or longer than the limit.
    public static string Require(string? value, int maxLength, string what)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0 || name.Length > maxLength)
            throw new ApiException(StatusCodes.Status400BadRequest, $"A {what} name needs 1 to {maxLength} characters.");
        return name;
    }
}
