using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Filtered RAG search over the unified document registry. Retrieval is
/// local (Ollama embed + pgvector, with a full-text fallback); synthesis via
/// Vertex Gemini only when explicitly requested with synthesize=true.
/// </summary>
[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly SearchService _search;

    public SearchController(SearchService search) => _search = search;

    public sealed record SearchForm(
        string Query, string[]? SourceTypes = null, string[]? Tags = null,
        string? Jurisdiction = null, int? YearFrom = null, int? YearTo = null,
        int TopK = 8, bool Synthesize = false, double MinScore = 0);

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Query))
            return BadRequest(new { detail = "query is required" });
        return Ok(await _search.SearchAsync(new SearchService.SearchRequest(
            form.Query.Trim(), form.SourceTypes, form.Tags, form.Jurisdiction,
            form.YearFrom, form.YearTo, form.TopK, form.Synthesize, form.MinScore), ct));
    }
}
