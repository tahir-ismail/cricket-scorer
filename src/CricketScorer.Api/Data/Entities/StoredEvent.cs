namespace CricketScorer.Api.Data;

public sealed class StoredEvent
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int Sequence { get; set; }
    public Guid SessionId { get; set; }
    public string Type { get; set; } = "";
    public string Json { get; set; } = "";
    public bool IsRemoved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
