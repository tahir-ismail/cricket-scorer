using System.Collections.Immutable;

namespace CricketScorer.Engine;

public enum InningsEndReason
{
    AllOut,
    OversComplete,
    TargetReached,
    Declared,
    Manual
}

public sealed record Extras(int Wides, int NoBalls, int Byes, int LegByes, int Penalties)
{
    public static readonly Extras None = new(0, 0, 0, 0, 0);

    public int Total => Wides + NoBalls + Byes + LegByes + Penalties;
}

public sealed record InningsState(
    Guid BattingTeamId,
    Guid BowlingTeamId,
    ImmutableList<Guid> BattingOrder,
    int Runs,
    int Wickets,
    int LegalBalls,
    int BallsPerOver,
    Extras Extras,
    ImmutableList<BatterScore> Batters,
    ImmutableList<BowlerFigures> Bowlers,
    ImmutableList<FallOfWicket> FallOfWickets,
    Guid? StrikerId,
    Guid? NonStrikerId,
    Guid? BowlerId,
    Guid? PreviousOverBowlerId,
    ImmutableList<string> ThisOver,
    int RunsThisOverOffBowler,
    bool OverShared,
    bool FreeHitNext,
    int? OversLimit,
    int? MaxOversPerBowler,
    int? Target,
    bool TargetSetByScorer,
    InningsEndReason? EndReason)
{
    public bool IsComplete => EndReason != null;

    // Players who haven't batted yet, in batting order.
    public IEnumerable<Guid> AvailableBatters =>
        BattingOrder.Where(id => Batters.All(b => b.PlayerId != id));

    // Players who retired hurt and haven't come back.
    public IEnumerable<Guid> RetiredHurtBatters =>
        Batters.Where(b => b.Status == BatterStatus.RetiredHurt).Select(b => b.PlayerId);

    // Overs bowled so far, e.g. "14.3".
    public string Overs => OversText(LegalBalls, BallsPerOver);

    // Runs per over so far, to 2 decimal places.
    public decimal RunRate => LegalBalls == 0 ? 0m : Math.Round(Runs * (decimal)BallsPerOver / LegalBalls, 2);

    // Runs still needed to reach the target, or null without a target.
    public int? RunsNeeded => Target is int t ? Math.Max(0, t - Runs) : null;

    // Legal balls left in the innings, or null without an overs limit.
    public int? BallsLeft => OversLimit is int o ? Math.Max(0, o * BallsPerOver - LegalBalls) : null;

    // Runs per over needed to win, or null without both a target and an overs limit.
    public decimal? RequiredRunRate => RunsNeeded is int need && BallsLeft is int left && left > 0
        ? Math.Round(need * (decimal)BallsPerOver / left, 2)
        : null;

    // Formats a number of legal balls as overs, e.g. 21 balls of 6 is "3.3".
    public static string OversText(int legalBalls, int ballsPerOver) =>
        $"{legalBalls / ballsPerOver}.{legalBalls % ballsPerOver}";

    // Replaces one batter's line using the given change.
    internal InningsState UpdateBatter(Guid id, Func<BatterScore, BatterScore> change) =>
        this with { Batters = Batters.Select(b => b.PlayerId == id ? change(b) : b).ToImmutableList() };

    // Replaces one bowler's figures using the given change, adding the bowler if new.
    internal InningsState UpdateBowler(Guid id, Func<BowlerFigures, BowlerFigures> change)
    {
        var existing = Bowlers.FirstOrDefault(b => b.PlayerId == id);
        return existing is null
            ? this with { Bowlers = Bowlers.Add(change(BowlerFigures.Empty(id))) }
            : this with { Bowlers = Bowlers.Replace(existing, change(existing)) };
    }
}
