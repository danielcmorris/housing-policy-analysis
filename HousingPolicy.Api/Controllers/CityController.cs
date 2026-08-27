using HousingPolicy.Api.Options;
using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// City legislation (Legistar-backed) + the unified document registry.
/// The sync endpoint walks a configured city's recently-modified matters and
/// stores the housing-relevant ones; registry endpoints expose RAG-layer
/// stats and a full rebuild.
/// </summary>
[ApiController]
public sealed class CityController : ControllerBase
{
    private readonly CityService _cities;
    private readonly DocumentRegistryService _registry;
    private readonly CityOptions _opt;
    private readonly ILogger<CityController> _log;

    public CityController(CityService cities, DocumentRegistryService registry,
                          IOptions<CityOptions> options, ILogger<CityController> log)
    {
        _cities = cities;
        _registry = registry;
        _opt = options.Value;
        _log = log;
    }

    [HttpGet("api/cities")]
    public IActionResult Clients() =>
        Ok(_opt.Clients.Select(c => new { c.Key, c.Name, c.Jurisdiction }));

    [HttpGet("api/city-matters")]
    public async Task<IActionResult> List(
        string view = "public", string? client = null, string? q = null, int limit = 100)
    {
        if (view is not ("public" or "admin"))
            return BadRequest(new { detail = "view must be 'public' or 'admin'" });
        return Ok(await _cities.ListAsync(view, client, q, Math.Clamp(limit, 1, 500)));
    }

    [HttpPost("api/admin/cities/{client}/sync")]
    public async Task<IActionResult> Sync(string client, int days = 90, CancellationToken ct = default)
    {
        try
        {
            var result = await _cities.SyncAsync(client, Math.Clamp(days, 1, 3650), ct);
            return result is null
                ? NotFound(new { detail = $"no configured city '{client}'" })
                : Ok(result);
        }
        catch (CongressApiException ex)
        {
            _log.LogWarning(ex, "legistar error for {Client}", client);
            return StatusCode(StatusCodes.Status502BadGateway, new { detail = $"legistar error: {ex.Message}" });
        }
    }

    [HttpGet("api/admin/registry/stats")]
    public async Task<IActionResult> RegistryStats() => Ok(await _registry.StatsAsync());

    [HttpPost("api/admin/registry/rebuild")]
    public async Task<IActionResult> RegistryRebuild(CancellationToken ct = default) =>
        Ok(await _registry.RebuildAsync(ct));
}
