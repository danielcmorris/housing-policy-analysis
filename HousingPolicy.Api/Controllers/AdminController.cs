using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Curation endpoints for the legislation tracker, consumed by the Angular
/// /admin pages. The refresh/discover/add/track actions DO call congress.gov;
/// each run is bounded by the TrackerOptions caps.
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly TrackerService _tracker;
    private readonly ILogger<AdminController> _log;

    public AdminController(TrackerService tracker, ILogger<AdminController> log)
    {
        _tracker = tracker;
        _log = log;
    }

    public sealed record AddBillRequest(int Congress, string BillType, int BillNumber, bool Tracked);
    public sealed record TrackingRequest(bool Tracked);
    public sealed record DisplayRequest(bool Displayed);
    public sealed record PinRequest(bool Pinned);

    private static readonly HashSet<string> ValidBillTypes = new(StringComparer.Ordinal)
    {
        "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres",
    };

    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => Ok(await _tracker.StatsAsync());

    [HttpPost("refresh")]
    public Task<IActionResult> Refresh(int congress = 119, CancellationToken ct = default) =>
        Upstream(() => _tracker.RefreshTrackedAsync(congress, ct));

    [HttpPost("discover")]
    public Task<IActionResult> Discover(int congress = 119, int days = 30, CancellationToken ct = default) =>
        Upstream(() => _tracker.DiscoverNewAsync(congress, Math.Clamp(days, 1, 365), ct));

    [HttpPost("bills")]
    public Task<IActionResult> AddBill([FromBody] AddBillRequest body, CancellationToken ct = default)
    {
        var billType = body.BillType.ToLowerInvariant();
        if (!ValidBillTypes.Contains(billType))
            return Task.FromResult<IActionResult>(BadRequest(new { detail = $"invalid bill_type '{billType}'" }));
        return Upstream(async () =>
        {
            var slug = await _tracker.AddBillAsync(body.Congress, billType, body.BillNumber, body.Tracked, ct);
            if (slug is null)
                return (object?)null;
            return new { bill_id = slug, tracking_status = body.Tracked ? "tracked" : "untracked" };
        }, notFoundDetail: "bill not found upstream");
    }

    [HttpPost("bills/{billId}/refresh")]
    public Task<IActionResult> RefreshBill(string billId, CancellationToken ct = default) =>
        Upstream(() => _tracker.RefreshOneAsync(billId, ct), notFoundDetail: $"unknown bill_id '{billId}'");

    [HttpPost("bills/{billId}/tracking")]
    public Task<IActionResult> SetTracking(string billId, [FromBody] TrackingRequest body,
                                           CancellationToken ct = default) =>
        Upstream(() => _tracker.SetTrackingAsync(billId, body.Tracked, ct),
                 notFoundDetail: $"unknown bill_id '{billId}'");

    [HttpPost("bills/{billId}/display")]
    public async Task<IActionResult> SetDisplay(string billId, [FromBody] DisplayRequest body)
    {
        var result = await _tracker.SetDisplayAsync(billId, body.Displayed);
        return result is null ? NotFound(new { detail = $"unknown bill_id '{billId}'" }) : Ok(result);
    }

    [HttpPost("bills/{billId}/pin")]
    public async Task<IActionResult> SetPin(string billId, [FromBody] PinRequest body)
    {
        var result = await _tracker.SetPinnedAsync(billId, body.Pinned);
        return result is null ? NotFound(new { detail = $"unknown bill_id '{billId}'" }) : Ok(result);
    }

    /// <summary>Run a congress.gov-touching operation with the shared upstream error mapping.</summary>
    private async Task<IActionResult> Upstream<T>(Func<Task<T?>> action, string? notFoundDetail = null)
    {
        try
        {
            var result = await action();
            if (result is null && notFoundDetail is not null)
                return NotFound(new { detail = notFoundDetail });
            return Ok(result);
        }
        catch (RateLimitedException)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { detail = "congress.gov rate limit exceeded" });
        }
        catch (CongressApiException ex)
        {
            _log.LogWarning(ex, "congress.gov error");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { detail = $"congress.gov error: {ex.Message}" });
        }
    }
}
