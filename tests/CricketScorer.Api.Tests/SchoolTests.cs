using System.Net;
using System.Net.Http.Json;
using CricketScorer.Api.Infrastructure;
using CricketScorer.Api.Schools;
using Microsoft.AspNetCore.Mvc;

namespace CricketScorer.Api.Tests;

[Collection("Api")]
public sealed class SchoolTests(ApiFactory factory)
{
    // Reads the caller's school.
    private static async Task<SchoolDto> MySchool(Scorer s) =>
        await (await s.Client.GetAsync("/api/schools/me")).ReadAsync<SchoolDto>();

    // Sends a join request with the given code.
    private static Task<HttpResponseMessage> Join(Scorer s, string code) =>
        s.Client.PostAsJsonAsync("/api/schools/join", new JoinSchoolRequest(code), TestApi.Json);

    // Checks creating a school makes the creator its admin and shows them the join code.
    [Fact]
    public async Task CreatorIsAdminAndSeesJoinCode()
    {
        var admin = await factory.NewSchoolAdminAsync("Westville");
        var school = await MySchool(admin);
        Assert.Equal("Westville", school.Name);
        Assert.True(school.IsAdmin);
        Assert.Equal(8, school.JoinCode!.Length);
        Assert.All(school.JoinCode, c => Assert.Contains(c, CodeGenerator.Alphabet));
        Assert.Single(school.Members);
    }

    // Checks another scorer can join with the code, in lower case with spaces.
    [Fact]
    public async Task ScorerJoinsWithCodeCaseInsensitive()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var code = (await MySchool(admin)).JoinCode!;
        var scorer = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await Join(scorer, $" {code.ToLowerInvariant()} ")).StatusCode);
        var school = await MySchool(scorer);
        Assert.False(school.IsAdmin);
        Assert.Null(school.JoinCode);
        Assert.Equal(2, school.Members.Count);
    }

    // Checks a scorer can't be in two schools.
    [Fact]
    public async Task CantJoinASecondSchool()
    {
        var first = await factory.NewSchoolAdminAsync();
        var second = await factory.NewSchoolAdminAsync("Glenwood");
        var code = (await MySchool(second)).JoinCode!;
        Assert.Equal(HttpStatusCode.Conflict, (await Join(first, code)).StatusCode);
        var create = await first.Client.PostAsJsonAsync("/api/schools", new CreateSchoolRequest("Another"), TestApi.Json);
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
    }

    // Checks resetting the join code stops the old one working.
    [Fact]
    public async Task ResetJoinCodeInvalidatesOldCode()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var oldCode = (await MySchool(admin)).JoinCode!;
        var reset = await admin.Client.PostAsync("/api/schools/me/join-code", null);
        var newCode = (await reset.ReadAsync<JoinCodeDto>()).JoinCode;
        Assert.NotEqual(oldCode, newCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Join(await factory.RegisterAsync(), oldCode)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Join(await factory.RegisterAsync(), newCode)).StatusCode);
    }

    // Checks only the admin can reset the code or remove members.
    [Fact]
    public async Task NonAdminCantManageSchool()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var member = await factory.JoinAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsync("/api/schools/me/join-code", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.DeleteAsync($"/api/schools/me/members/{admin.UserId}")).StatusCode);
    }

    // Checks the admin can't remove themselves.
    [Fact]
    public async Task AdminCantRemoveThemselves()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var response = await admin.Client.DeleteAsync($"/api/schools/me/members/{admin.UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Checks removing someone who isn't in the school is a 404.
    [Fact]
    public async Task RemovingStrangerIs404()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var stranger = await factory.RegisterAsync();
        var response = await admin.Client.DeleteAsync($"/api/schools/me/members/{stranger.UserId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Checks a removed scorer loses access straight away even with a valid token.
    [Fact]
    public async Task RemovedScorerLosesAccessImmediately()
    {
        var admin = await factory.NewSchoolAdminAsync();
        var member = await factory.JoinAsync(admin);
        var match = await admin.CreateMatchAsync();
        Assert.Equal(HttpStatusCode.OK, (await member.Client.GetAsync($"/api/matches/{match.Id}")).StatusCode);
        var remove = await admin.Client.DeleteAsync($"/api/schools/me/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var response = await member.Client.GetAsync("/api/schools/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Join or create a school first.", (await response.ReadAsync<ProblemDetails>()).Title);
        Assert.Single((await MySchool(admin)).Members);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync($"/api/matches/{match.Id}")).StatusCode);
    }

    // Checks a scorer without a school is told to join one.
    [Fact]
    public async Task NoSchoolGets403()
    {
        var scorer = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await scorer.Client.GetAsync("/api/schools/me")).StatusCode);
    }
}
