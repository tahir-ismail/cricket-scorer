using Microsoft.AspNetCore.Identity;

namespace CricketScorer.Api.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }
}
