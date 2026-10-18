namespace CricketScorer.Engine.Tests;

public sealed class MatchHarness
{
    private readonly List<MatchEvent> _events = new();

    public MatchSetup Setup { get; }
    public Guid TeamA => Setup.TeamA.TeamId;
    public Guid TeamB => Setup.TeamB.TeamId;
    public ImmutableList<Guid> A => Setup.TeamA.Players;
    public ImmutableList<Guid> B => Setup.TeamB.Players;
    public MatchState State { get; private set; }
    public IReadOnlyList<MatchEvent> Events => _events;
    public InningsState Inn => State.Innings[^1];

    // Creates a harness with two fresh teams of the given size.
    private MatchHarness(MatchSettings settings, int xiSize)
    {
        TeamLineup Lineup() =>
            new(Guid.NewGuid(), Enumerable.Range(0, xiSize).Select(_ => Guid.NewGuid()).ToImmutableList());
        Setup = new MatchSetup(settings, Lineup(), Lineup());
        State = MatchState.Initial(Setup);
    }

    // Creates a harness for a match, defaulting to T20 with 11 players a side.
    public static MatchHarness Create(MatchSettings? settings = null, int xiSize = 11) =>
        new(settings ?? MatchSettings.ForFormat(MatchFormat.T20), xiSize);

    // Starts an innings with the first two batters and the fielding side's last player bowling.
    public MatchHarness Start(bool teamABats = true)
    {
        var (team, batting, fielding) = teamABats ? (TeamA, A, B) : (TeamB, B, A);
        return Send(new InningsStarted(Guid.NewGuid(), team, batting[0], batting[1], fielding[^1]));
    }

    // Applies an event and records it.
    public MatchHarness Send(MatchEvent e)
    {
        State = MatchEngine.Apply(State, e);
        _events.Add(e);
        return this;
    }

    // Bowls one ball.
    public MatchHarness Bowl(int bat = 0, ExtraType extra = ExtraType.None, int extraRuns = 0,
        bool boundary = false, Dismissal? d = null) =>
        Send(new BallBowled(Guid.NewGuid(), bat, extra, extraRuns, boundary, d));

    // Bowls a number of dot balls.
    public MatchHarness Dots(int n)
    {
        for (var i = 0; i < n; i++) Bowl();
        return this;
    }

    // Brings on a new bowler.
    public MatchHarness NewBowler(Guid id) => Send(new BowlerChanged(Guid.NewGuid(), id));

    // Sends in a new batter.
    public MatchHarness In(Guid id) => Send(new BatterIn(Guid.NewGuid(), id));

    // Removes the last event and rebuilds the state from the rest.
    public MatchHarness Undo()
    {
        _events.RemoveAt(_events.Count - 1);
        State = MatchEngine.Replay(Setup, _events);
        return this;
    }

    // Returns the error an event would cause, or null if it's allowed.
    public string? Try(MatchEvent e) => MatchEngine.Validate(State, e);

    // Returns the score card line for a batter in the current innings.
    public BatterScore Batter(Guid id) => Inn.Batters.Single(b => b.PlayerId == id);

    // Returns the figures for a bowler in the current innings.
    public BowlerFigures Bowler(Guid id) => Inn.Bowlers.Single(b => b.PlayerId == id);
}
