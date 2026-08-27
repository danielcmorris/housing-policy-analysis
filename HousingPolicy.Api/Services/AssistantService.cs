using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Document-scoped assistant chat: the ENTIRE text of one bill / study /
/// city matter rides in Gemini's context window alongside the running
/// conversation. Answers are constrained to the document; outside knowledge
/// is permitted only when the user explicitly asks for comparisons or
/// further data, and must be labeled as such. Every call is token-assessed
/// against the chat cap and recorded in ai_usage.
/// </summary>
public sealed class AssistantService
{
    private readonly DataLayerBase _dl;
    private readonly GeminiClient _gemini;
    private readonly GeminiOptions _opt;

    public AssistantService(DataLayerBase dl, GeminiClient gemini, IOptions<GeminiOptions> options)
    {
        _dl = dl;
        _gemini = gemini;
        _opt = options.Value;
    }

    public sealed record DocContext(
        string SourceType, string SourceKey, string Title, string Kind,
        string LinkKind, string LinkHref, bool HasText, int TokenEstimate);

    private sealed record DocText(string Title, string? Text, string? ReviewId,
                                  int? CityMatterId, string? CityClient);

    /// <summary>Resolve a document's title, text, and link-back target.</summary>
    public async Task<DocContext?> GetContextAsync(string sourceType, string sourceKey)
    {
        var doc = await LoadAsync(sourceType, sourceKey);
        if (doc is null) return null;

        var (linkKind, linkHref) = sourceType switch
        {
            "study" => ("internal", $"/studies/{sourceKey}"),
            "federal_bill" when doc.ReviewId is not null => ("internal", $"/bills/{doc.ReviewId}"),
            "federal_bill" => ("external", FederalUrl(sourceKey)),
            "city_matter" when doc is { CityClient: not null, CityMatterId: not null } =>
                ("external", $"https://{doc.CityClient}.legistar.com/gateway.aspx?m=l&id={doc.CityMatterId}"),
            _ => ("internal", "/"),
        };
        var kind = sourceType switch
        {
            "study" => "study",
            "city_matter" => "city matter",
            _ => "bill",
        };
        return new DocContext(sourceType, sourceKey, doc.Title, kind, linkKind, linkHref,
                              !string.IsNullOrEmpty(doc.Text), (doc.Text?.Length ?? 0) / 4);
    }

    public sealed record ChatTurn(string Role, string Text);
    public sealed record ChatResult(string Text, string Model, int InputTokens, int OutputTokens,
                                    bool DocumentTruncated);

    public async Task<ChatResult?> ChatAsync(
        string sourceType, string sourceKey, IReadOnlyList<ChatTurn> messages, CancellationToken ct)
    {
        var doc = await LoadAsync(sourceType, sourceKey);
        if (doc is null || string.IsNullOrEmpty(doc.Text)) return null;

        var kind = sourceType == "study" ? "study" : sourceType == "city_matter" ? "city matter" : "bill";

        // Keep the running dialog bounded; the document dominates the budget.
        var turns = messages.TakeLast(12)
            .Select(m => (m.Role == "ai" || m.Role == "model" ? "model" : "user", m.Text))
            .ToList();
        var dialogChars = turns.Sum(t => t.Item2.Length);

        // Pre-call token assessment: the whole document goes in unless it
        // exceeds the hard cap, in which case it is truncated with a visible
        // marker rather than silently.
        var budgetChars = (_opt.MaxChatInputTokens * 4) - dialogChars - 2000;
        var text = doc.Text;
        var truncated = false;
        if (text.Length > budgetChars)
        {
            text = text[..Math.Max(budgetChars, 4000)] +
                   "\n\n[DOCUMENT TRUNCATED HERE TO FIT THE CONTEXT LIMIT]";
            truncated = true;
        }

        var system =
            $"You are the research assistant of a non-partisan housing-policy institute. " +
            $"The user is reading the {kind} \"{doc.Title}\". The complete text is below. " +
            $"Answer strictly and only from this text: quote or reference specific sections " +
            $"where possible, and if the text does not contain the answer, say so plainly. " +
            $"You may draw on knowledge beyond the document ONLY when the user explicitly asks " +
            $"for comparisons, context, or further data — and when you do, clearly label that " +
            $"material as going beyond the document.\n\n" +
            $"=== FULL TEXT OF THE {kind.ToUpperInvariant()} ===\n{text}";

        var result = await _gemini.GenerateChatAsync(system, turns, ct);

        await _dl.ExecuteAsync(
            """
            INSERT INTO ai_usage (provider, model, purpose, input_tokens, output_tokens)
            VALUES ('vertex_gemini', @Model, 'doc_chat', @In, @Out)
            """,
            new { Model = _opt.Model, In = result.InputTokens, Out = result.OutputTokens });

        return new ChatResult(result.Text, _opt.Model, result.InputTokens, result.OutputTokens, truncated);
    }

    private async Task<DocText?> LoadAsync(string sourceType, string sourceKey) => sourceType switch
    {
        "federal_bill" => await _dl.QuerySingleOrDefaultAsync<DocText>(
            """
            SELECT b.title,
                   (SELECT tv.text_content FROM bill_text_versions tv
                    WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL
                    ORDER BY tv.version_date DESC NULLS LAST LIMIT 1) AS text,
                   br.review_id, NULL::int AS city_matter_id, NULL AS city_client
            FROM bills b
            LEFT JOIN bill_reviews br ON br.bill_id = b.bill_id
            WHERE b.bill_id = @Key
            """, new { Key = sourceKey }),
        "study" => await _dl.QuerySingleOrDefaultAsync<DocText>(
            """
            SELECT title, text_content AS text, NULL AS review_id,
                   NULL::int AS city_matter_id, NULL AS city_client
            FROM studies WHERE ref = @Key
            """, new { Key = sourceKey }),
        "city_matter" => await _dl.QuerySingleOrDefaultAsync<DocText>(
            """
            SELECT title, text_content AS text, NULL AS review_id,
                   matter_id AS city_matter_id, client AS city_client
            FROM city_matters WHERE city_matter_id = @Key
            """, new { Key = sourceKey }),
        _ => null,
    };

    private static string FederalUrl(string sourceKey)
    {
        var parts = sourceKey.Split('-');
        return parts.Length == 3 && int.TryParse(parts[0], out var congress) && int.TryParse(parts[2], out var number)
            ? TrackerRules.CongressGovUrl(congress, parts[1], number)
            : "https://www.congress.gov";
    }
}
