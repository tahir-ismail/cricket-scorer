namespace CricketScorer.Api.Infrastructure;

public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }

    // Creates an error that the API turns into a response with this status code, message and optional machine-readable code.
    public ApiException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
