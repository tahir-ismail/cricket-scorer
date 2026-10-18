using System.Net;
using CricketScorer.Api.Scorecards;
using CricketScorer.Engine;
using static CricketScorer.Api.Tests.TestApi;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class ScorecardTests(ApiFactory factory)
{
    private static readonly Guid Batter = Guid.NewGuid(), Bowler = Guid.NewGuid(), Keeper = Guid.NewGuid();
    private static readonly Guid Home = Guid.NewGuid(), Away = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [Batter] = "Smith", [Bowler] = "Steyn", [Keeper] = "Boucher"
    };

    private static readonly Dictionary<Guid, string> Teams = new() { [Home] = "Westville U16A", [Away] = "Glenwood U16A" };

    // Builds a batter line with the given status and dismissal.
    private static BatterScore Line(BatterStatus status, DismissalType? type = null, Guid? fielder = null, bool bowlerCredited = true) =>
        new(Batter, 10, 12, 1, 0, status, type is null ? null : new Dismissal(type.Value, Batter, fielder),
            bowlerCredited && type is not null ? Bowler : null);

    public static TheoryData<BatterScore, string> HowOutCases => new()
    {
        { Line(BatterStatus.Batting), "not out" },
        { Line(BatterStatus.Out, DismissalType.Bowled), "b Steyn" },
        { Line(BatterStatus.Out, DismissalType.Caught, Keeper), "c Boucher b Steyn" },
        { Line(BatterStatus.Out, DismissalType.Caught, Bowler), "c & b Steyn" },
        { Line(BatterStatus.Out, DismissalType.Lbw), "lbw b Steyn" },
        { Line(BatterStatus.Out, DismissalType.Stumped, Keeper), "st Boucher b Steyn" },
        { Line(BatterStatus.Out, DismissalType.HitWicket), "hit wicket b Steyn" },
        { Line(BatterStatus.Out, DismissalType.RunOut, Keeper, bowlerCredited: false), "run out (Boucher)" },
        { Line(BatterStatus.Out, DismissalType.RunOut, bowlerCredited: false), "run out" },
        { Line(BatterStatus.Out, DismissalType.ObstructingField, bowlerCredited: false), "obstructing the field" },
        { Line(BatterStatus.Out, DismissalType.Other, bowlerCredited: false), "out" },
        { Line(BatterStatus.RetiredHurt), "retired hurt" },
        { Line(BatterStatus.RetiredOut), "retired out" }
    };

    // Checks every dismissal type produces the right "how out" text.
    [Theory]
    [MemberData(nameof(HowOutCases))]
    public void HowOutText(BatterScore line, string expected) =>
        Assert.Equal(expected, ScorecardBuilder.HowOut(line, Names));

    public static TheoryData<MatchResult?, string?> ResultCases => new()
    {
        { new MatchResult(ResultType.Win, Home, 1, null, null), "Westville U16A won by 1 run" },
        { new MatchResult(ResultType.Win, Home, 24, null, null), "Westville U16A won by 24 runs" },
        { new MatchResult(ResultType.Win, Away, null, 1, null), "Glenwood U16A won by 1 wicket" },
        { new MatchResult(ResultType.Win, Away, null, 6, null), "Glenwood U16A won by 6 wickets" },
        { new MatchResult(ResultType.Tie, null, null, null, null), "Match tied" },
        { new MatchResult(ResultType.Draw, null, null, null, null), "Match drawn" },
        { new MatchResult(ResultType.NoResult, null, null, null, null), "No result" },
        { new MatchResult(ResultType.Draw, null, null, null, "Westville won on first innings"), "Westville won on first innings" },
        { null, null }
    };

    // Checks the result wording, including singular run and wicket.
    [Theory]
    [MemberData(nameof(ResultCases))]
    public void ResultTextWording(MatchResult? result, string? expected) =>
        Assert.Equal(expected, ScorecardBuilder.ResultText(result, Teams));

    // Plays a short innings with every kind of extra and a caught wicket.
    private static async Task PlayShortInningsAsync(Scorer admin, TestMatch m)
    {
        var result = await admin.SyncAsync(m,
            Add(1, StartFor(m)),
            Add(2, Ball(4, true)),
            Add(3, Ball(6, true)),
            Add(4, new BallBowled(Guid.NewGuid(), Extra: ExtraType.Wide)),
            Add(5, new BallBowled(Guid.NewGuid(), 1, ExtraType.NoBall)),
            Add(6, new BallBowled(Guid.NewGuid(), Extra: ExtraType.Bye, ExtraRuns: 1)),
            Add(7, new BallBowled(Guid.NewGuid(), Dismissal: new Dismissal(DismissalType.Caught, m.A[0], m.B[0]))),
            Add(8, new BatterIn(Guid.NewGuid(), m.A[2])));
        Assert.Null((await result.ReadAsync<Sync.SyncResult>()).Rejected);
    }

    // Checks a scorecard adds up: batter runs + extras = total, and bowler runs = total minus byes, leg byes and penalties.
    [Fact]
    public async Task ScorecardTotalsAddUp()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var m = await admin.CreateMatchAsync();
        await PlayShortInningsAsync(admin, m);

        var card = await (await admin.Client.GetAsync($"/api/matches/{m.Id}/scorecard")).ReadAsync<ScorecardDto>();
        var inn = Assert.Single(card.Innings);
        Assert.Equal((14, 1, "0.4"), (inn.Runs, inn.Wickets, inn.Overs));
        Assert.Equal(new ExtrasDto(1, 1, 1, 0, 0, 3), inn.Extras);
        Assert.Equal(inn.Runs, inn.Batting.Sum(b => b.Runs) + inn.Extras.Total);
        var bowler = Assert.Single(inn.Bowling);
        Assert.Equal(inn.Runs - inn.Extras.Byes - inn.Extras.LegByes - inn.Extras.Penalties, bowler.Runs);
        Assert.Equal((1, "0.4", 1, 1), (bowler.Wickets, bowler.Overs, bowler.Wides, bowler.NoBalls));

        var detail = await (await admin.Client.GetAsync($"/api/matches/{m.Id}")).ReadAsync<Matches.MatchDetailDto>();
        var fielding = detail.TeamB.Players;
        var opener = inn.Batting[0];
        Assert.Equal($"c {fielding[0].Name} b {fielding[^1].Name}", opener.HowOut);
        Assert.Equal((11, 4, 1, 1), (opener.Runs, opener.Balls, opener.Fours, opener.Sixes));
        Assert.Equal(275m, opener.StrikeRate);
        Assert.Equal("not out", inn.Batting[1].HowOut);
        Assert.Equal(8, inn.DidNotBat.Count);
        Assert.Equal(new FallOfWicketDto(1, 14, inn.Batting[0].Name, "0.4"), Assert.Single(inn.FallOfWickets));
        Assert.Null(card.ResultText);
        Assert.NotNull(card.LastUpdated);
    }

    // Checks the share link works with no login and an unknown code is 404.
    [Fact]
    public async Task ShareScorecardIsPublic()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var m = await admin.CreateMatchAsync();
        await PlayShortInningsAsync(admin, m);
        var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/share/{m.ShareCode}/scorecard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(14, Assert.Single((await response.ReadAsync<ScorecardDto>()).Innings).Runs);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/share/NOPE/scorecard")).StatusCode);
    }

    // Checks the share link can't be used to change the match.
    [Fact]
    public async Task ShareLinkIsReadOnly()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var m = await admin.CreateMatchAsync();
        var anonymous = factory.CreateClient();
        var post = await anonymous.PostAsync($"/api/share/{m.ShareCode}/scorecard", null);
        Assert.False(post.IsSuccessStatusCode);
        var sync = await anonymous.PostAsync($"/api/matches/{m.Id}/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, sync.StatusCode);
        var card = await (await admin.Client.GetAsync($"/api/matches/{m.Id}/scorecard")).ReadAsync<ScorecardDto>();
        Assert.Empty(card.Innings);
    }

    // Checks another school can't read the scorecard by match ID.
    [Fact]
    public async Task OtherSchoolGets404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var m = await admin.CreateMatchAsync();
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/matches/{m.Id}/scorecard")).StatusCode);
    }
}
