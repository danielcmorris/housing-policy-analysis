using Dapper;
using HousingPolicy.Api.Modules;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>Liveness + DB ping. Port of the Python /health endpoint.</summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly DataLayerBase _dl;

    public HealthController(DataLayerBase dl) => _dl = dl;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        await using var con = await _dl.OpenConnectionAsync(ct);
        await con.ExecuteScalarAsync<int>("SELECT 1");
        return Ok(new { status = "ok" });
    }
}
