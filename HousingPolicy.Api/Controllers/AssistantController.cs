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

    public sealed record ChatMessage(string Role, string Text);
    public sealed record ChatForm(string SourceType, string SourceKey, List<ChatMessage> Messages);

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatForm form, CancellationToken ct)
    {
        if (!ValidTypes.Contains(form.SourceType))
            return BadRequest(new { detail = $"unknown source_type '{form.SourceType}'" });
        if (form.Messages.Count == 0 || form.Messages[^1].Role != "user")
            return BadRequest(new { detail = "messages must end with a user turn" });

        try
        {
            var result = await _assistant.ChatAsync(
                form.SourceType, form.SourceKey,
                form.Messages.Select(m => new AssistantService.ChatTurn(m.Role, m.Text)).ToList(), ct);
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
