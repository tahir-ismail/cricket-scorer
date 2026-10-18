namespace CricketScorer.Api.Data;

public sealed class Team
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = "";
    public bool IsOpponent { get; set; }
    public List<Player> Players { get; set; } = [];
}
