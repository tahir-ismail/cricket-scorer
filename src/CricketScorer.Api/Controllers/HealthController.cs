using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CricketScorer.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    // Answers that the API is running.
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
