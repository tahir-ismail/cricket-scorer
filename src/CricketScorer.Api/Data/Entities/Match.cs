using System.ComponentModel.DataAnnotations;
using CricketScorer.Engine;

namespace CricketScorer.Api.Data;

public enum TossDecision
{
    Bat,
    Bowl
}

public enum MatchStatus
{
    Upcoming,
    Live,
    Finished
}

public sealed class Match
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ShareCode { get; set; } = "";
    public DateOnly Date { get; set; }
    public string? Venue { get; set; }
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid? TossWinnerTeamId { get; set; }
    public TossDecision? TossDecision { get; set; }
    public MatchFormat Format { get; set; }
    public int? OversPerSide { get; set; }
    public int? MaxOversPerBowler { get; set; }
    public int BallsPerOver { get; set; }
    public bool FreeHitsEnabled { get; set; }
    public bool DeclarationsAllowed { get; set; }
    public MatchStatus Status { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [Timestamp]
    public byte[] Version { get; set; } = [];

    public List<MatchPlayer> Players { get; set; } = [];
}
