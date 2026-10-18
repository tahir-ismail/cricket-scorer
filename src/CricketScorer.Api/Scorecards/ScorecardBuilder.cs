using CricketScorer.Api.Matches;
using CricketScorer.Engine;

namespace CricketScorer.Api.Scorecards;

public static class ScorecardBuilder
{
    // Builds the full scorecard for a match from its replayed state.
    public static ScorecardDto Build(LoadedMatch m, IReadOnlyDictionary<Guid, string> playerNames,
        IReadOnlyDictionary<Guid, string> teamNames, DateTimeOffset? lastUpdated)
    {
        var match = m.Match;
        return new ScorecardDto(match.Id, teamNames[match.TeamAId], teamNames[match.TeamBId], match.Date, match.Venue,
            match.Status, ResultText(m.State.Result, teamNames),
            m.State.Innings.Select(i => Innings(i, playerNames, teamNames)).ToList(), lastUpdated);
    }

    // Describes how a batter got out, e.g. "c Boucher b Steyn".
    public static string HowOut(BatterScore b, IReadOnlyDictionary<Guid, string> names)
    {
        if (b.Status == BatterStatus.Batting) return "not out";
        if (b.Status == BatterStatus.RetiredHurt) return "retired hurt";
        if (b.Status == BatterStatus.RetiredOut) return "retired out";

        var d = b.Dismissal!;
        var bowler = b.DismissedByBowlerId is Guid bowlerId ? names[bowlerId] : "";
        var fielder = d.FielderId is Guid fielderId ? names[fielderId] : null;
        return d.Type switch
        {
            DismissalType.Bowled => $"b {bowler}",
            DismissalType.Caught when d.FielderId == b.DismissedByBowlerId => $"c & b {bowler}",
            DismissalType.Caught => $"c {fielder} b {bowler}",
            DismissalType.Lbw => $"lbw b {bowler}",
            DismissalType.Stumped => $"st {fielder} b {bowler}",
            DismissalType.HitWicket => $"hit wicket b {bowler}",
            DismissalType.RunOut => fielder is null ? "run out" : $"run out ({fielder})",
            DismissalType.ObstructingField => "obstructing the field",
            _ => "out"
        };
    }

    // Describes the result, e.g. "Westville U16A won by 24 runs"; a scorer's override text wins.
    public static string? ResultText(MatchResult? r, IReadOnlyDictionary<Guid, string> teamNames)
    {
        if (r is null) return null;
        if (!string.IsNullOrWhiteSpace(r.OverrideText)) return r.OverrideText;
        return r.Type switch
        {
            ResultType.Win when r.ByRuns is int runs => $"{teamNames[r.WinnerTeamId!.Value]} won by {runs} run{(runs == 1 ? "" : "s")}",
            ResultType.Win when r.ByWickets is int wkts => $"{teamNames[r.WinnerTeamId!.Value]} won by {wkts} wicket{(wkts == 1 ? "" : "s")}",
            ResultType.Win => $"{teamNames[r.WinnerTeamId!.Value]} won",
            ResultType.Tie => "Match tied",
            ResultType.Draw => "Match drawn",
            _ => "No result"
        };
    }

    // Builds one innings' batting, bowling, extras and fall of wickets.
    private static InningsCardDto Innings(InningsState inn, IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> teamNames)
    {
        var e = inn.Extras;
        return new InningsCardDto(teamNames[inn.BattingTeamId], inn.Runs, inn.Wickets, inn.Overs, inn.RunRate, inn.Target,
            new ExtrasDto(e.Wides, e.NoBalls, e.Byes, e.LegByes, e.Penalties, e.Total),
            inn.Batters.Select(b => new BatterLineDto(names[b.PlayerId], HowOut(b, names), b.Runs, b.BallsFaced, b.Fours, b.Sixes,
                Rate(b.Runs * 100m, b.BallsFaced))).ToList(),
            inn.AvailableBatters.Select(id => names[id]).ToList(),
            inn.Bowlers.Select(b => new BowlerLineDto(names[b.PlayerId], InningsState.OversText(b.LegalBalls, inn.BallsPerOver),
                b.Maidens, b.RunsConceded, b.Wickets, Rate(b.RunsConceded * (decimal)inn.BallsPerOver, b.LegalBalls),
                b.Wides, b.NoBalls)).ToList(),
            inn.FallOfWickets.Select(f => new FallOfWicketDto(f.WicketNumber, f.Score, names[f.PlayerId],
                InningsState.OversText(f.LegalBallsAtFall, inn.BallsPerOver))).ToList(),
            inn.IsComplete);
    }

    // Divides to 2 decimal places, or 0 when there's nothing to divide by.
    private static decimal Rate(decimal amount, int per) => per == 0 ? 0m : Math.Round(amount / per, 2);
}
