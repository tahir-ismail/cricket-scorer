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
    bool FreeHitNext,
    int? OversLimit,
    int? MaxOversPerBowler,
    int? Target,
    InningsEndReason? EndReason)
{
    public bool IsComplete => EndReason != null;

    // Players who haven't batted yet, in batting order.
    public IEnumerable<Guid> AvailableBatters =>
        BattingOrder.Where(id => Batters.All(b => b.PlayerId != id));

    // Players who retired hurt and haven't come back.
    public IEnumerable<Guid> RetiredHurtBatters =>
        Batters.Where(b => b.Status == BatterStatus.RetiredHurt).Select(b => b.PlayerId);

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
