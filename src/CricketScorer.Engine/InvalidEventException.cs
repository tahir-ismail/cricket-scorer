namespace CricketScorer.Engine;

public sealed class InvalidEventException : Exception
{
    // Creates the exception with the reason the event was rejected.
    public InvalidEventException(string message) : base(message)
    {
    }
}
