using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Document-scoped assistant chat: the ENTIRE text of one bill / study /
/// city matter rides in Gemini's context window alongside the running
/// conversation, optionally joined by up to four comparison documents.
/// Comparison docs are tiered against the input-token budget: full text
/// while it fits, otherwise only their chunks most relevant to the latest
/// question (vector-retrieved). Answers are constrained to the provided
/// texts; outside knowledge is permitted only when the user explicitly asks
/// for it, and must be labeled as such. Every call is token-assessed
/// against the chat cap and recorded in ai_usage.
/// </summary>
public sealed class AssistantService
{
    private const int MaxCompareDocs = 4;
    private const int ExcerptChunks = 8;

    private readonly DataLayerBase _dl;
    private readonly GeminiClient _gemini;
    private readonly GeminiOptions _opt;
    private readonly OllamaEmbedClient _ollama;
    private readonly VertexEmbedClient _vertex;
    private readonly EmbeddingOptions _embedOpt;

    public AssistantService(DataLayerBase dl, GeminiClient gemini, IOptions<GeminiOptions> options,
                            OllamaEmbedClient ollama, VertexEmbedClient vertex,
                            IOptions<EmbeddingOptions> embedOpt)
    {
        _dl = dl;
        _gemini = gemini;
        _opt = options.Value;
        _ollama = ollama;
        _vertex = vertex;
        _embedOpt = embedOpt.Value;
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
        return new DocContext(sourceType, sourceKey, doc.Title, Kind(sourceType), linkKind, linkHref,
                              !string.IsNullOrEmpty(doc.Text), (doc.Text?.Length ?? 0) / 4);
    }

    private static string Kind(string sourceType) => sourceType switch
    {
        "study" => "study",
        "city_matter" => "city matter",
        _ => "bill",
    };

    // --- related documents ---------------------------------------------------

    public sealed class RelatedDoc
    {
        public string SourceType { get; set; } = "";
        public string SourceKey { get; set; } = "";
        public string? Title { get; set; }
        public string? Jurisdiction { get; set; }
        public int? DocYear { get; set; }
        public string Relation { get; set; } = "similar";  // 'precedent' | 'related' | ... | 'similar'
        public double? Similarity { get; set; }            // null for curated rows
        public int TokenEstimate { get; set; }
    }

    /// <summary>
    /// Documents worth comparing against: curated document_relations rows
    /// first (either direction), then embedding-similarity fill — the
    /// primary's chunk centroid against every other document's best chunk.
    /// </summary>
    public async Task<List<RelatedDoc>?> RelatedAsync(string sourceType, string sourceKey, int topK)
    {
        var docId = await _dl.QuerySingleOrDefaultAsync<long?>(
            "SELECT document_id FROM documents WHERE source_type = @St AND source_key = @Sk",
            new { St = sourceType, Sk = sourceKey });
        if (docId is null) return null;

        const string tokenEstimate = """
            (SELECT COALESCE(sum(c.token_estimate), 0)::int FROM document_chunks c
             WHERE c.document_id = d.document_id)
            """;

        var curated = (await _dl.QueryAsync<RelatedDoc>(
            $"""
            SELECT d.source_type, d.source_key, d.title, d.jurisdiction, d.doc_year,
                   r.relation, NULL::float AS similarity, {tokenEstimate} AS token_estimate
            FROM document_relations r
            JOIN documents d ON d.document_id =
                 CASE WHEN r.from_document_id = @Id THEN r.to_document_id ELSE r.from_document_id END
            WHERE @Id IN (r.from_document_id, r.to_document_id)
              AND {DocumentRegistryService.PublishedOnly}
            ORDER BY r.relation, d.title
            LIMIT @TopK
            """, new { Id = docId, TopK = topK })).ToList();

        var remaining = topK - curated.Count;
        if (remaining > 0)
        {
            var seenKeys = curated.Select(c => c.SourceType + "/" + c.SourceKey).ToArray();
            var similar = (await _dl.QueryAsync<RelatedDoc>(
                $"""
                WITH centroid AS (
                    SELECT avg(embedding) AS v FROM document_chunks
                    WHERE document_id = @Id AND embedding IS NOT NULL AND embedding_model = @Model
                )
                SELECT d.source_type, d.source_key, d.title, d.jurisdiction, d.doc_year,
                       'similar' AS relation,
                       max(1 - (c.embedding <=> (SELECT v FROM centroid))) AS similarity,
                       {tokenEstimate} AS token_estimate
                FROM document_chunks c
                JOIN documents d ON d.document_id = c.document_id
                WHERE c.embedding IS NOT NULL AND c.embedding_model = @Model
                  AND d.document_id <> @Id
                  AND (d.source_type || '/' || d.source_key) <> ALL(@Seen)
                  AND (SELECT v FROM centroid) IS NOT NULL
                  AND {DocumentRegistryService.PublishedOnly}
                GROUP BY d.document_id, d.source_type, d.source_key, d.title, d.jurisdiction, d.doc_year
                ORDER BY similarity DESC
                LIMIT @TopK
                """,
                new { Id = docId, Model = ActiveEmbedModel, Seen = seenKeys, TopK = remaining })).ToList();
            curated.AddRange(similar);
        }
        return curated;
    }

    // --- chat ----------------------------------------------------------------

    public sealed record ChatTurn(string Role, string Text);
    public sealed record CompareRef(string SourceType, string SourceKey);
    public sealed record ContextDoc(string SourceType, string SourceKey, string Title, string Mode, int Tokens);
    public sealed record ChatResult(string Text, string Model, int InputTokens, int OutputTokens,
                                    bool DocumentTruncated, List<ContextDoc> ContextDocs);

    public async Task<ChatResult?> ChatAsync(
        string sourceType, string sourceKey, IReadOnlyList<ChatTurn> messages,
        IReadOnlyList<CompareRef>? compare, CancellationToken ct)
    {
        var doc = await LoadAsync(sourceType, sourceKey);
        if (doc is null || string.IsNullOrEmpty(doc.Text)) return null;

        var kind = Kind(sourceType);

        // Keep the running dialog bounded; the documents dominate the budget.
        var turns = messages.TakeLast(12)
            .Select(m => (m.Role == "ai" || m.Role == "model" ? "model" : "user", m.Text))
            .ToList();
        var dialogChars = turns.Sum(t => t.Item2.Length);

        // Pre-call token assessment: the primary document goes in whole unless
        // it alone exceeds the hard cap, in which case it is truncated with a
        // visible marker rather than silently.
        var budgetChars = (_opt.MaxChatInputTokens * 4) - dialogChars - 2000;
        var text = doc.Text;
        var truncated = false;
        if (text.Length > budgetChars)
        {
            text = text[..Math.Max(budgetChars, 4000)] +
                   "\n\n[DOCUMENT TRUNCATED HERE TO FIT THE CONTEXT LIMIT]";
            truncated = true;
        }
        var remainingChars = budgetChars - text.Length;

        // Tiered comparison fill: full text while it fits the remaining
        // budget, otherwise only the chunks most relevant to the latest
        // question (labeled so the model — and the user — know the doc is
        // partially represented).
        var contextDocs = new List<ContextDoc>();
        var compareSections = new System.Text.StringBuilder();
        if (compare is { Count: > 0 })
        {
            var question = messages[^1].Text;
            float[]? questionVec = null;
            var vecTried = false;
            var n = 0;
            foreach (var cmp in compare.Take(MaxCompareDocs))
            {
                if (cmp.SourceType == sourceType && cmp.SourceKey == sourceKey) continue;
                var other = await LoadAsync(cmp.SourceType, cmp.SourceKey);
                if (other is null || string.IsNullOrEmpty(other.Text)) continue;
                n++;
                var otherKind = Kind(cmp.SourceType);

                if (other.Text.Length + 200 <= remainingChars)
                {
                    var section = $"\n\n=== COMPARISON {n}: {otherKind.ToUpperInvariant()} \"{other.Title}\" (FULL TEXT) ===\n{other.Text}";
                    compareSections.Append(section);
                    remainingChars -= section.Length;
                    contextDocs.Add(new ContextDoc(cmp.SourceType, cmp.SourceKey, other.Title, "full",
                                                   other.Text.Length / 4));
                    continue;
                }

                if (!vecTried)
                {
                    vecTried = true;
                    questionVec = await EmbedQueryAsync(question, ct);
                }
                var excerpts = questionVec is not null
                    ? await ExcerptsAsync(cmp.SourceType, cmp.SourceKey, questionVec, ct)
                    : new List<string>();
                // No embeddings reachable: fall back to the document's opening
                // so a comparison is still possible, clearly bounded.
                if (excerpts.Count == 0)
                    excerpts = new List<string> { other.Text[..Math.Min(other.Text.Length, ExcerptChunks * 1600)] };

                var body = new System.Text.StringBuilder();
                foreach (var e in excerpts)
                {
                    if (body.Length + e.Length > Math.Max(remainingChars - 400, 2000)) break;
                    body.Append(e).Append("\n\n[…]\n\n");
                }
                if (body.Length == 0) continue; // budget exhausted entirely

                var sec = $"\n\n=== COMPARISON {n}: {otherKind.ToUpperInvariant()} \"{other.Title}\" (RELEVANT EXCERPTS ONLY) ===\n{body}";
                compareSections.Append(sec);
                remainingChars -= sec.Length;
                contextDocs.Add(new ContextDoc(cmp.SourceType, cmp.SourceKey, other.Title, "excerpts",
                                               (int)body.Length / 4));
            }
        }

        var comparing = contextDocs.Count > 0;
        var system =
            $"You are the research assistant of a non-partisan housing-policy institute. " +
            $"The user is reading the {kind} \"{doc.Title}\". The complete text is below" +
            (comparing
                ? $", followed by {contextDocs.Count} comparison document(s) the user selected. " +
                  $"Ground every answer — including comparisons — strictly in the provided texts: " +
                  $"quote or reference specific sections where possible, and if the texts do not " +
                  $"contain the answer, say so plainly. Documents marked RELEVANT EXCERPTS ONLY are " +
                  $"only partially represented; when drawing on one, note that your view of it is " +
                  $"limited to excerpts. "
                : $". Answer strictly and only from this text: quote or reference specific sections " +
                  $"where possible, and if the text does not contain the answer, say so plainly. ") +
            $"You may draw on knowledge beyond the provided documents ONLY when the user explicitly " +
            $"asks for comparisons, context, or further data beyond them — and when you do, clearly " +
            $"label that material as going beyond the documents.\n\n" +
            $"=== FULL TEXT OF THE {kind.ToUpperInvariant()} ===\n{text}" +
            compareSections;

        var result = await _gemini.GenerateChatAsync(system, turns, ct);

        await _dl.ExecuteAsync(
            """
            INSERT INTO ai_usage (provider, model, purpose, input_tokens, output_tokens)
            VALUES ('vertex_gemini', @Model, 'doc_chat', @In, @Out)
            """,
            new { Model = _opt.Model, In = result.InputTokens, Out = result.OutputTokens });

        return new ChatResult(result.Text, _opt.Model, result.InputTokens, result.OutputTokens,
                              truncated, contextDocs);
    }

    /// <summary>Top chunks of one document by cosine distance to the question.</summary>
    private async Task<List<string>> ExcerptsAsync(
        string sourceType, string sourceKey, float[] questionVec, CancellationToken ct)
    {
        var p = new DynamicParameters();
        p.Add("Query", "[" + string.Join(',', questionVec.Select(v => v.ToString("G7"))) + "]");
        p.Add("St", sourceType);
        p.Add("Sk", sourceKey);
        p.Add("Model", ActiveEmbedModel);
        p.Add("K", ExcerptChunks);
        return (await _dl.QueryAsync<string>(
            $"""
            SELECT c.content
            FROM document_chunks c
            JOIN documents d ON d.document_id = c.document_id
            WHERE d.source_type = @St AND d.source_key = @Sk
              AND c.embedding IS NOT NULL AND c.embedding_model = @Model
              AND {DocumentRegistryService.PublishedOnly}
            ORDER BY c.embedding <=> @Query::vector
            LIMIT @K
            """, p)).ToList();
    }

    private Task<float[]?> EmbedQueryAsync(string query, CancellationToken ct) =>
        _embedOpt.Provider == "ollama" ? _ollama.EmbedAsync(query, ct) : _vertex.EmbedQueryAsync(query, ct);

    private string ActiveEmbedModel =>
        _embedOpt.Provider == "ollama" ? "nomic-embed-text" : _vertex.Model;

    /// <summary>Published documents only — the assistant is a public surface,
    /// so an unpublished source key resolves like it doesn't exist.</summary>
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
              AND b.display_date IS NOT NULL AND b.display_date <= now()
            """, new { Key = sourceKey }),
        "study" => await _dl.QuerySingleOrDefaultAsync<DocText>(
            """
            SELECT title, text_content AS text, NULL AS review_id,
                   NULL::int AS city_matter_id, NULL AS city_client
            FROM studies WHERE ref = @Key
              AND display_date IS NOT NULL AND display_date <= now()
            """, new { Key = sourceKey }),
        "city_matter" => await _dl.QuerySingleOrDefaultAsync<DocText>(
            """
            SELECT title, text_content AS text, NULL AS review_id,
                   matter_id AS city_matter_id, client AS city_client
            FROM city_matters WHERE city_matter_id = @Key
              AND display_date IS NOT NULL AND display_date <= now()
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
