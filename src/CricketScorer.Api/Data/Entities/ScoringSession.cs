namespace CricketScorer.Api.Data;

public sealed class ScoringSession
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public string? DeviceLabel { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
