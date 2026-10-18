namespace CricketScorer.Api.Data;

public sealed class Player
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
}
