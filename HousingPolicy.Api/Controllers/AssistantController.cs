using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Document-scoped assistant chat. GET context resolves the document's title
/// and link-back; POST chat sends the full document text plus the running
/// dialog to Gemini (answers constrained to the document).
/// </summary>
[ApiController]
[Route("api/assistant")]
public sealed class AssistantController : ControllerBase
{
    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal)
    {
        "federal_bill", "study", "city_matter",
    };

    private readonly AssistantService _assistant;
    private readonly ILogger<AssistantController> _log;

    public AssistantController(AssistantService assistant, ILogger<AssistantController> log)
    {
        _assistant = assistant;
        _log = log;
    }

    [HttpGet("context")]
    public async Task<IActionResult> Context(string source_type, string source_key)
    {
        if (!ValidTypes.Contains(source_type))
            return BadRequest(new { detail = $"unknown source_type '{source_type}'" });
        var ctx = await _assistant.GetContextAsync(source_type, source_key);
        return ctx is null
            ? NotFound(new { detail = $"no document '{source_type}/{source_key}'" })
            : Ok(ctx);
    }

    /// <summary>Candidate comparison documents: curated relations + embedding similarity.</summary>
    [HttpGet("related")]
    public async Task<IActionResult> Related(string source_type, string source_key, int top_k = 5)
    {
        if (!ValidTypes.Contains(source_type))
            return BadRequest(new { detail = $"unknown source_type '{source_type}'" });
        var related = await _assistant.RelatedAsync(source_type, source_key, Math.Clamp(top_k, 1, 12));
        return related is null
            ? NotFound(new { detail = $"no registry document '{source_type}/{source_key}'" })
            : Ok(new { related });
    }

    public sealed record ChatMessage(string Role, string Text);
    public sealed record CompareDoc(string SourceType, string SourceKey);
    public sealed record ChatForm(string SourceType, string SourceKey, List<ChatMessage> Messages,
                                  List<CompareDoc>? Compare);

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatForm form, CancellationToken ct)
    {
        if (!ValidTypes.Contains(form.SourceType))
            return BadRequest(new { detail = $"unknown source_type '{form.SourceType}'" });
        if (form.Messages.Count == 0 || form.Messages[^1].Role != "user")
            return BadRequest(new { detail = "messages must end with a user turn" });
        if (form.Compare is { Count: > 4 })
            return BadRequest(new { detail = "at most 4 comparison documents" });
        if (form.Compare?.Any(c => !ValidTypes.Contains(c.SourceType)) == true)
            return BadRequest(new { detail = "unknown source_type in compare list" });

        try
        {
            var result = await _assistant.ChatAsync(
                form.SourceType, form.SourceKey,
                form.Messages.Select(m => new AssistantService.ChatTurn(m.Role, m.Text)).ToList(),
                form.Compare?.Select(c => new AssistantService.CompareRef(c.SourceType, c.SourceKey)).ToList(),
                ct);
            return result is null
                ? NotFound(new { detail = "document not found or has no stored text" })
                : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "assistant chat failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { detail = ex.Message });
        }
    }
}
