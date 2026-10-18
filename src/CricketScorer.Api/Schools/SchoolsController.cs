using CricketScorer.Api.Auth;
using CricketScorer.Api.Data;
using CricketScorer.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Schools;

[ApiController]
[Route("api/schools")]
public sealed class SchoolsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    public const int JoinCodeLength = 8;

    // Creates a school and makes the caller its admin.
    [HttpPost]
    public async Task<ActionResult<SchoolDto>> Create(CreateSchoolRequest request)
    {
        var user = await current.GetAsync();
        if (user.SchoolId is not null) throw new ApiException(StatusCodes.Status409Conflict, "You're already in a school.");
        var name = Names.Require(request.Name, 100, "school");

        var school = new School
        {
            Id = Guid.NewGuid(),
            Name = name,
            JoinCode = await NewJoinCodeAsync(),
            AdminUserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Schools.Add(school);
        user.SchoolId = school.Id;
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Mine), await ToDtoAsync(school, user.Id));
    }

    // Adds the caller to the school with this join code.
    [HttpPost("join")]
    public async Task<SchoolDto> Join(JoinSchoolRequest request)
    {
        var user = await current.GetAsync();
        if (user.SchoolId is not null) throw new ApiException(StatusCodes.Status409Conflict, "You're already in a school.");
        var code = request.JoinCode?.Trim().ToUpperInvariant() ?? "";
        var school = await db.Schools.SingleOrDefaultAsync(s => s.JoinCode == code)
                     ?? throw new ApiException(StatusCodes.Status404NotFound, "No school has that join code.");
        user.SchoolId = school.Id;
        await db.SaveChangesAsync();
        return await ToDtoAsync(school, user.Id);
    }

    // Returns the caller's school and its members; only the admin sees the join code.
    [HttpGet("me")]
    public async Task<SchoolDto> Mine()
    {
        var school = await MySchoolAsync();
        return await ToDtoAsync(school, current.UserId);
    }

    // Gives the school a new join code so the old one stops working (admin only).
    [HttpPost("me/join-code")]
    public async Task<JoinCodeDto> ResetJoinCode()
    {
        var school = await MyAdminSchoolAsync();
        school.JoinCode = await NewJoinCodeAsync();
        await db.SaveChangesAsync();
        return new JoinCodeDto(school.JoinCode);
    }

    // Removes a scorer from the school (admin only, and not themselves).
    [HttpDelete("me/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid userId)
    {
        var school = await MyAdminSchoolAsync();
        if (userId == school.AdminUserId) throw new ApiException(StatusCodes.Status400BadRequest, "The admin can't remove themselves.");
        var member = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.SchoolId == school.Id)
                     ?? throw new ApiException(StatusCodes.Status404NotFound, "That scorer isn't in your school.");
        member.SchoolId = null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Loads the caller's school, refusing if they don't have one.
    private async Task<School> MySchoolAsync()
    {
        var schoolId = await current.RequireSchoolIdAsync();
        return await db.Schools.SingleAsync(s => s.Id == schoolId);
    }

    // Loads the caller's school, refusing if they aren't its admin.
    private async Task<School> MyAdminSchoolAsync()
    {
        var school = await MySchoolAsync();
        if (school.AdminUserId != current.UserId)
            throw new ApiException(StatusCodes.Status403Forbidden, "Only the school admin can do that.");
        return school;
    }

    // Makes a join code that no other school is using.
    private async Task<string> NewJoinCodeAsync()
    {
        string code;
        do code = CodeGenerator.New(JoinCodeLength);
        while (await db.Schools.AnyAsync(s => s.JoinCode == code));
        return code;
    }

    // Builds the school response, showing the join code only to the admin.
    private async Task<SchoolDto> ToDtoAsync(School school, Guid viewerId)
    {
        var members = await db.Users
            .Where(u => u.SchoolId == school.Id)
            .OrderBy(u => u.DisplayName)
            .Select(u => new MemberDto(u.Id, u.DisplayName, u.Email!, u.Id == school.AdminUserId))
            .ToListAsync();
        var isAdmin = viewerId == school.AdminUserId;
        return new SchoolDto(school.Id, school.Name, isAdmin, isAdmin ? school.JoinCode : null, members);
    }
}
