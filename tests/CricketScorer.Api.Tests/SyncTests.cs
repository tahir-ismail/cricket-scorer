using System.Net;
using System.Net.Http.Json;
using System.Text;
using CricketScorer.Api.Data;
using CricketScorer.Api.Matches;
using CricketScorer.Api.Sync;
using CricketScorer.Engine;
using Microsoft.AspNetCore.Mvc;
using static CricketScorer.Api.Tests.TestApi;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class SyncTests(ApiFactory factory)
{
    // Reads a match's details.
    private static async Task<MatchDetailDto> Detail(Scorer s, Guid id) =>
        await (await s.Client.GetAsync($"/api/matches/{id}")).ReadAsync<MatchDetailDto>();

    // Syncs operations and reads the result, failing if the request itself was refused.
    private static async Task<SyncResult> Sync(Scorer s, TestMatch m, params SyncOp[] ops)
    {
        var response = await s.SyncAsync(m, ops);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SyncResult>();
    }

    // Creates a school admin with a fresh match.
    private async Task<(Scorer Admin, TestMatch Match)> NewMatchAsync()
    {
        var admin = await factory.NewSchoolAdminAsync();
        return (admin, await admin.CreateMatchAsync());
    }

    // Checks a batch of valid events is saved and the match goes live.
    [Fact]
    public async Task ValidEventsAreSavedAndMatchGoesLive()
    {
        var (admin, m) = await NewMatchAsync();
        var result = await Sync(admin, m, Add(1, StartFor(m)), Add(2, Ball(1)), Add(3, Ball(4, true)), Add(4, Ball()));
        Assert.Equal(new SyncResult(4, 0, 4, null), result);
        var detail = await Detail(admin, m.Id);
        Assert.Equal([1, 2, 3, 4], detail.Events.Select(e => e.Sequence));
        Assert.Equal(MatchStatus.Live, detail.Status);
    }

    // Checks sending the same batch twice is harmless.
    [Fact]
    public async Task SameBatchTwiceIsSkipped()
    {
        var (admin, m) = await NewMatchAsync();
        SyncOp[] ops = [Add(1, StartFor(m)), Add(2, Ball(1)), Add(3, Ball(2)), Add(4, Ball())];
        await Sync(admin, m, ops);
        Assert.Equal(new SyncResult(0, 4, 4, null), await Sync(admin, m, ops));
        Assert.Equal(4, (await Detail(admin, m.Id)).Events.Count);
    }

    // Checks a retried batch containing an undo ends in the same place.
    [Fact]
    public async Task RetriedBatchWithUndoLeavesSameEvents()
    {
        var (admin, m) = await NewMatchAsync();
        var start = StartFor(m);
        var x = Ball(1);
        SyncOp[] batch = [Add(1, start), Add(2, x), Remove(x.Id)];
        await Sync(admin, m, batch);
        Assert.Equal(new SyncResult(0, 3, 1, null), await Sync(admin, m, batch));
        var y = Ball(2);
        Assert.Equal(new SyncResult(1, 0, 2, null), await Sync(admin, m, Add(2, y)));
        Assert.Equal([start.Id, y.Id], (await Detail(admin, m.Id)).Events.Select(e => e.Event.Id));
    }

    // Checks a gap in the sequence is rejected.
    [Fact]
    public async Task SequenceGapIsRejected()
    {
        var (admin, m) = await NewMatchAsync();
        var ball = Ball();
        var result = await Sync(admin, m, Add(1, StartFor(m)), Add(3, ball));
        Assert.Equal(new SyncResult(1, 0, 1, new SyncRejection(1, ball.Id, "Expected event 2.")), result);
    }

    // Checks the engine's reason comes back for an illegal event and later operations are not applied.
    [Fact]
    public async Task EngineRejectsIllegalEventAndStops()
    {
        var (admin, m) = await NewMatchAsync();
        var second = StartFor(m);
        var result = await Sync(admin, m, Add(1, StartFor(m)), Add(2, second), Add(3, Ball()));
        Assert.Equal(new SyncResult(1, 0, 1, new SyncRejection(1, second.Id, "An innings is already in progress.")), result);
        Assert.Single((await Detail(admin, m.Id)).Events);
    }

    // Checks undo removes the last event and frees its sequence number.
    [Fact]
    public async Task UndoRemovesLastEvent()
    {
        var (admin, m) = await NewMatchAsync();
        var ball = Ball(6, true);
        await Sync(admin, m, Add(1, StartFor(m)), Add(2, ball));
        Assert.Equal(new SyncResult(1, 0, 1, null), await Sync(admin, m, Remove(ball.Id)));
        Assert.Equal(new SyncResult(1, 0, 2, null), await Sync(admin, m, Add(2, Ball(1))));
    }

    // Checks only the last event can be undone, and an unknown event can't be undone.
    [Fact]
    public async Task UndoOfEarlierEventIsRejected()
    {
        var (admin, m) = await NewMatchAsync();
        var start = StartFor(m);
        await Sync(admin, m, Add(1, start), Add(2, Ball()));
        Assert.Equal(new SyncRejection(0, start.Id, "Only the last event can be undone."), (await Sync(admin, m, Remove(start.Id))).Rejected);
        var unknown = Guid.NewGuid();
        Assert.Equal(new SyncRejection(0, unknown, "Unknown event."), (await Sync(admin, m, Remove(unknown))).Rejected);
    }

    // Checks the same event can't be saved at a second position.
    [Fact]
    public async Task SameEventAtAnotherPositionIsRejected()
    {
        var (admin, m) = await NewMatchAsync();
        var ball = Ball();
        await Sync(admin, m, Add(1, StartFor(m)), Add(2, ball));
        var result = await Sync(admin, m, Add(3, ball));
        Assert.Equal(new SyncRejection(0, ball.Id, "That event is already saved at position 2."), result.Rejected);
    }

    // Checks an event ID from another match is refused.
    [Fact]
    public async Task EventIdUsedInOtherMatchIsRejected()
    {
        var (admin, m1) = await NewMatchAsync();
        var m2 = await admin.CreateMatchAsync();
        var start = StartFor(m1);
        await Sync(admin, m1, Add(1, start));
        var clash = start with { BattingTeamId = m2.TeamA, StrikerId = m2.A[0], NonStrikerId = m2.A[1], BowlerId = m2.B[^1] };
        var result = await Sync(admin, m2, Add(1, clash));
        Assert.Equal(new SyncRejection(0, start.Id, "That event ID is already used by another match."), result.Rejected);
        Assert.Equal(start.Id, Assert.Single((await Detail(admin, m1.Id)).Events).Event.Id);
        Assert.Empty((await Detail(admin, m2.Id)).Events);
    }

    // Checks a device whose session was taken over can't save anything.
    [Fact]
    public async Task OldSessionGets409AndNothingSaved()
    {
        var (admin, m) = await NewMatchAsync();
        await Sync(admin, m, Add(1, StartFor(m)));
        var takeover = await admin.Client.PostAsJsonAsync($"/api/matches/{m.Id}/sessions", new StartSessionRequest("iPad"), Json);
        Assert.Equal(HttpStatusCode.Created, takeover.StatusCode);

        var response = await admin.SyncAsync(m, Add(2, Ball()));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal("Another device has taken over scoring.", problem.Title);
        Assert.Equal("session_replaced", problem.Extensions["code"]?.ToString());
        Assert.Single((await Detail(admin, m.Id)).Events);

        var newSession = (await takeover.ReadAsync<SessionStartedDto>()).SessionId;
        Assert.Equal(HttpStatusCode.OK, (await admin.SyncAsync(m, newSession, Add(2, Ball()))).StatusCode);
    }

    // Checks a school mate can't sync someone else's match, and another school gets 404.
    [Fact]
    public async Task NonOwnerGets403()
    {
        var (admin, m) = await NewMatchAsync();
        var member = await factory.JoinAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.SyncAsync(m, Add(1, StartFor(m)))).StatusCode);
        var other = await factory.NewSchoolAdminAsync("Glenwood");
        Assert.Equal(HttpStatusCode.NotFound, (await other.SyncAsync(m, Add(1, StartFor(m)))).StatusCode);
        Assert.Empty((await Detail(admin, m.Id)).Events);
    }

    // Checks broken request bodies are 400, not 500.
    [Fact]
    public async Task MalformedSyncBodiesReturn400()
    {
        var (admin, m) = await NewMatchAsync();
        var url = $"/api/matches/{m.Id}/sync";
        var unknownType = $$$"""{"sessionId":"{{{m.SessionId}}}","ops":[{"kind":"add","sequence":1,"event":{"type":"teleport","id":"{{{Guid.NewGuid()}}}"}}]}""";
        var noEvent = $$$"""{"sessionId":"{{{m.SessionId}}}","ops":[{"kind":"add","sequence":1}]}""";
        var badKind = $$$"""{"sessionId":"{{{m.SessionId}}}","ops":[{"kind":"zap","sequence":1}]}""";
        var noOps = $$$"""{"sessionId":"{{{m.SessionId}}}","ops":[]}""";
        foreach (var body in new[] { unknownType, noEvent, badKind, noOps })
        {
            var response = await admin.Client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var tooMany = Enumerable.Range(1, SyncController.MaxOps + 1).Select(i => Add(i, Ball())).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SyncAsync(m, tooMany)).StatusCode);
        Assert.Empty((await Detail(admin, m.Id)).Events);
    }

    // Checks closing the match marks it finished.
    [Fact]
    public async Task MatchClosedMarksFinished()
    {
        var (admin, m) = await NewMatchAsync();
        await Sync(admin, m, Add(1, StartFor(m)), Add(2, Ball(2)),
            Add(3, new InningsEnded(Guid.NewGuid(), InningsEndReason.Manual)), Add(4, new MatchClosed(Guid.NewGuid())));
        Assert.Equal(MatchStatus.Finished, (await Detail(admin, m.Id)).Status);
    }
}
