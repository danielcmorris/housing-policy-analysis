using HousingPolicy.Api.Models;
using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Fetch-and-store one federal law. Serves the stored copy unless ?refresh=true,
/// otherwise pulls metadata + full text bodies from congress.gov, persists them,
/// and returns the normalized record. (Port of the Python routers/bills.py.)
/// </summary>
[ApiController]
[Route("api/bills")]
public sealed class BillsController : ControllerBase
{
    private static readonly HashSet<string> ValidBillTypes = new(StringComparer.Ordinal)
    {
        "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres",
    };

    private readonly CongressClient _congress;
    private readonly BillRepository _repo;
    private readonly ILogger<BillsController> _log;

    public BillsController(CongressClient congress, BillRepository repo, ILogger<BillsController> log)
    {
        _congress = congress;
        _repo = repo;
        _log = log;
    }

    [HttpGet("{congress:int}/{billType}/{billNumber:int}")]
    [ProducesResponseType(typeof(Bill), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLaw(
        int congress, string billType, int billNumber, bool refresh, CancellationToken ct)
    {
        billType = billType.ToLowerInvariant();
        if (!ValidBillTypes.Contains(billType))
            return BadRequest(new { detail = $"invalid bill_type '{billType}'" });

        var slug = BillRepository.BillSlug(congress, billType, billNumber);

        if (!refresh)
        {
            var existing = await _repo.GetBillAsync(slug);
            if (existing is not null)
                return Ok(existing);
        }

        try
        {
            var billJson = await _congress.FetchBillAsync(congress, billType, billNumber, refresh, ct);
            var textJson = await _congress.FetchBillTextAsync(congress, billType, billNumber, refresh, ct);

            // Metadata + the single most-recent raw text (one body fetch, not all versions).
            var latest = BillRepository.SelectLatestFormattedText(textJson);
            string? body = null;
            if (latest is not null && !string.IsNullOrEmpty(latest.Url))
                body = await _congress.FetchTextBodyAsync(latest.Url, ct);

            await _repo.StoreLawAsync(congress, billType, billNumber, billJson, textJson, latest, body, ct);
        }
        catch (BillNotFoundException)
        {
            return NotFound(new { detail = $"bill {slug} not found upstream" });
        }
        catch (RateLimitedException)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { detail = "congress.gov rate limit exceeded" });
        }
        catch (CongressApiException ex)
        {
            _log.LogWarning(ex, "congress.gov error for {Slug}", slug);
            return StatusCode(StatusCodes.Status502BadGateway, new { detail = $"congress.gov error: {ex.Message}" });
        }

        var result = await _repo.GetBillAsync(slug);
        return Ok(result);
    }
}
