namespace CricketScorer.Api.Data;

public sealed class MatchPlayer
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public Guid PlayerId { get; set; }
    public int Order { get; set; }
}
