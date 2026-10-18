namespace CricketScorer.Api.Infrastructure;

public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    // Creates an error that the API turns into a response with this status code and message.
    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
