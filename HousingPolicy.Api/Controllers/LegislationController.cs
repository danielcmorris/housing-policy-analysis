using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// The legislation-tracker feed. Read-only: never calls congress.gov.
/// Public view is decided by display_date (pinned bills first); admin view
/// sees everything with the tracking filter applied.
/// </summary>
[ApiController]
[Route("api/legislation")]
public sealed class LegislationController : ControllerBase
{
    private readonly TrackerService _tracker;

    public LegislationController(TrackerService tracker) => _tracker = tracker;

    [HttpGet]
    public async Task<IActionResult> Get(
        int? congress,
        string view = "public",
        string tracking = "all",
        string? q = null,
        int limit = 50)
    {
        if (view is not ("public" or "admin"))
            return BadRequest(new { detail = "view must be 'public' or 'admin'" });
        if (tracking is not ("tracked" or "untracked" or "all"))
            return BadRequest(new { detail = "tracking must be 'tracked', 'untracked', or 'all'" });
        limit = Math.Clamp(limit, 1, 200);

        return Ok(await _tracker.ListAsync(view, tracking, q, congress, limit));
    }
}
