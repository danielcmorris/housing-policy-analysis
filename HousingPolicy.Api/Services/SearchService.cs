using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Filtered RAG search over the unified document registry.
///
/// Retrieval is local and free: the query embeds via Ollama (same model as
/// the chunks) and pgvector ranks by cosine distance, AFTER the cheap SQL
/// filters (source types, tags, jurisdiction, years). When Ollama is
/// unreachable or chunks aren't embedded yet, ranking falls back to Postgres
/// full-text search so the endpoint still works.
///
/// Synthesis is the explicit extra step: the top chunks go to Vertex Gemini
/// with a pre-call token assessment against hard caps, and every call lands
/// in the ai_usage ledger.
/// </summary>
public sealed class SearchService
{
    private readonly DataLayerBase _dl;
    private readonly OllamaEmbedClient _ollama;
    private readonly GeminiClient _gemini;
    private readonly GeminiOptions _geminiOpt;

    public SearchService(DataLayerBase dl, OllamaEmbedClient ollama, GeminiClient gemini,
                         IOptions<GeminiOptions> geminiOpt)
    {
        _dl = dl;
        _ollama = ollama;
        _gemini = gemini;
        _geminiOpt = geminiOpt.Value;
    }

    public sealed record SearchRequest(
        string Query, string[]? SourceTypes, string[]? Tags, string? Jurisdiction,
        int? YearFrom, int? YearTo, int TopK = 8, bool Synthesize = false);

    public sealed class Hit
    {
        public long ChunkId { get; set; }
        public long DocumentId { get; set; }
        public string SourceType { get; set; } = "";
        public string SourceKey { get; set; } = "";
        public string? Title { get; set; }
        public string? Jurisdiction { get; set; }
        public int? DocYear { get; set; }
        public string Content { get; set; } = "";
        public double Score { get; set; }
    }

    public async Task<object> SearchAsync(SearchRequest req, CancellationToken ct)
    {
        var topK = Math.Clamp(req.TopK, 1, 25);
        var vector = await _ollama.EmbedAsync(req.Query, ct);
        var (hits, mode) = vector is not null
            ? (await VectorSearchAsync(req, vector, topK), "vector")
            : (await KeywordSearchAsync(req, topK), "keyword");

        object? answer = null;
        string? synthesisError = null;
        if (req.Synthesize && hits.Count > 0)
        {
            try
            {
                answer = await SynthesizeAsync(req.Query, hits, ct);
            }
            catch (InvalidOperationException ex)
            {
                synthesisError = ex.Message;
            }
        }

        return new
        {
            mode,
            query = req.Query,
            results = hits.Select(h => new
            {
                chunk_id = h.ChunkId,
                document_id = h.DocumentId,
                source_type = h.SourceType,
                source_key = h.SourceKey,
                title = h.Title,
                jurisdiction = h.Jurisdiction,
                doc_year = h.DocYear,
                snippet = h.Content.Length > 400 ? h.Content[..400] + "…" : h.Content,
                score = Math.Round(h.Score, 4),
            }),
            answer,
            synthesis_error = synthesisError,
        };
    }

    private const string HitColumns = """
        c.chunk_id, d.document_id, d.source_type, d.source_key, d.title,
        d.jurisdiction, d.doc_year, c.content
        """;

    private static string Filters(SearchRequest req, DynamicParameters p)
    {
        var sql = "";
        if (req.SourceTypes is { Length: > 0 })
        {
            sql += " AND d.source_type = ANY(@SourceTypes)";
            p.Add("SourceTypes", req.SourceTypes);
        }
        if (req.Tags is { Length: > 0 })
        {
            sql += """
                 AND EXISTS (SELECT 1 FROM document_tags dt JOIN tags t ON t.tag_id = dt.tag_id
                             WHERE dt.document_id = d.document_id AND t.name = ANY(@Tags))
                """;
            p.Add("Tags", req.Tags);
        }
        if (!string.IsNullOrWhiteSpace(req.Jurisdiction))
        {
            sql += " AND d.jurisdiction ILIKE @Jurisdiction";
            p.Add("Jurisdiction", $"%{req.Jurisdiction.Trim()}%");
        }
        if (req.YearFrom is not null) { sql += " AND d.doc_year >= @YearFrom"; p.Add("YearFrom", req.YearFrom); }
        if (req.YearTo is not null) { sql += " AND d.doc_year <= @YearTo"; p.Add("YearTo", req.YearTo); }
        return sql;
    }

    private async Task<List<Hit>> VectorSearchAsync(SearchRequest req, float[] vector, int topK)
    {
        var p = new DynamicParameters();
        p.Add("Query", "[" + string.Join(',', vector.Select(v => v.ToString("G7"))) + "]");
        var sql = $"""
            SELECT {HitColumns}, 1 - (c.embedding <=> @Query::vector) AS score
            FROM document_chunks c
            JOIN documents d ON d.document_id = c.document_id
            WHERE c.embedding IS NOT NULL
            """ + Filters(req, p) +
            " ORDER BY c.embedding <=> @Query::vector LIMIT @TopK";
        p.Add("TopK", topK);
        var hits = (await _dl.QueryAsync<Hit>(sql, p)).ToList();
        return hits;
    }

    private async Task<List<Hit>> KeywordSearchAsync(SearchRequest req, int topK)
    {
        // Precision pass first (all terms), then an any-term retry so long
        // natural-language questions still match.
        var strict = await KeywordQueryAsync(req, req.Query, topK, useOr: false);
        if (strict.Count > 0) return strict;
        return await KeywordQueryAsync(req, req.Query, topK, useOr: true);
    }

    private async Task<List<Hit>> KeywordQueryAsync(SearchRequest req, string query, int topK, bool useOr)
    {
        var q = query;
        if (useOr)
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
                .Where(t => t.Length > 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (terms.Length == 0) return new List<Hit>();
            q = string.Join(" OR ", terms);
        }
        var fn = useOr ? "websearch_to_tsquery" : "plainto_tsquery";
        var p = new DynamicParameters();
        p.Add("Q", q);
        var sql = $"""
            SELECT {HitColumns},
                   ts_rank(to_tsvector('english', c.content), {fn}('english', @Q)) AS score
            FROM document_chunks c
            JOIN documents d ON d.document_id = c.document_id
            WHERE to_tsvector('english', c.content) @@ {fn}('english', @Q)
            """ + Filters(req, p) +
            " ORDER BY score DESC LIMIT @TopK";
        p.Add("TopK", topK);
        return (await _dl.QueryAsync<Hit>(sql, p)).ToList();
    }

    private async Task<object> SynthesizeAsync(string query, List<Hit> hits, CancellationToken ct)
    {
        if (!_gemini.IsConfigured)
            throw new InvalidOperationException("Gemini is not configured (no service-account key in creds/).");

        var context = hits.Take(_geminiOpt.MaxContextChunks).ToList();
        const string system =
            "You are the research assistant of a non-partisan housing-policy institute. " +
            "Answer strictly from the provided source excerpts. Cite sources inline as " +
            "[source_key]. If the excerpts do not answer the question, say so plainly.";

        string Prompt(List<Hit> chunks) =>
            $"Question: {query}\n\nSource excerpts:\n" + string.Join("\n\n", chunks.Select((h, i) =>
                $"[{h.SourceKey}] ({h.SourceType}, {h.Jurisdiction} {h.DocYear}) {h.Title}\n{h.Content}"));

        // Pre-call token assessment against the hard input cap: trim context
        // until the estimate fits; refuse rather than exceed.
        var prompt = Prompt(context);
        while ((prompt.Length + system.Length) / 4 > _geminiOpt.MaxInputTokens && context.Count > 1)
        {
            context.RemoveAt(context.Count - 1);
            prompt = Prompt(context);
        }
        if ((prompt.Length + system.Length) / 4 > _geminiOpt.MaxInputTokens)
            throw new InvalidOperationException("query context exceeds the configured Gemini input-token cap");

        var result = await _gemini.GenerateAsync(system, prompt, ct);

        await _dl.ExecuteAsync(
            """
            INSERT INTO ai_usage (provider, model, purpose, input_tokens, output_tokens)
            VALUES ('vertex_gemini', @Model, 'search_synthesis', @In, @Out)
            """,
            new { Model = _geminiOpt.Model, In = result.InputTokens, Out = result.OutputTokens });

        return new
        {
            text = result.Text,
            model = _geminiOpt.Model,
            input_tokens = result.InputTokens,
            output_tokens = result.OutputTokens,
            context_chunks = context.Count,
            sources = context.Select(h => new { h.SourceType, h.SourceKey, h.Title }).Distinct(),
        };
    }
}
