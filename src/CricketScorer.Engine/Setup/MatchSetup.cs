using System.Collections.Immutable;

namespace CricketScorer.Engine;

public sealed record TeamLineup(Guid TeamId, ImmutableList<Guid> Players);

public sealed record MatchSetup(MatchSettings Settings, TeamLineup TeamA, TeamLineup TeamB)
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 15;

    // Returns an error message if the setup can't be used, or null if it's fine.
    public string? Validate()
    {
        if (TeamA.TeamId == TeamB.TeamId)
            return "A team can't play against itself.";

        foreach (var team in new[] { TeamA, TeamB })
        {
            if (team.Players.Count is < MinPlayers or > MaxPlayers)
                return $"A playing XI needs between {MinPlayers} and {MaxPlayers} players.";
            if (team.Players.Distinct().Count() != team.Players.Count)
                return "A player is listed twice in the same team.";
        }

        if (TeamA.Players.Intersect(TeamB.Players).Any())
            return "A player can't play for both teams.";

        return null;
    }

    // Finds the lineup for a team ID, or null if the team isn't in this match.
    public TeamLineup? LineupFor(Guid teamId) =>
        teamId == TeamA.TeamId ? TeamA : teamId == TeamB.TeamId ? TeamB : null;

    // Returns the lineup of the other team.
    public TeamLineup OpponentOf(Guid teamId) => teamId == TeamA.TeamId ? TeamB : TeamA;
}
