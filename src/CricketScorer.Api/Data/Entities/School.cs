namespace CricketScorer.Api.Data;

public sealed class School
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public Guid AdminUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
