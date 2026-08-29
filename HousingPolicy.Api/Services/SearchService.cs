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
    private readonly VertexEmbedClient _vertex;
    private readonly GeminiClient _gemini;
    private readonly GeminiOptions _geminiOpt;
    private readonly EmbeddingOptions _embedOpt;

    public SearchService(DataLayerBase dl, OllamaEmbedClient ollama, VertexEmbedClient vertex,
                         GeminiClient gemini, IOptions<GeminiOptions> geminiOpt,
                         IOptions<EmbeddingOptions> embedOpt)
    {
        _dl = dl;
        _ollama = ollama;
        _vertex = vertex;
        _gemini = gemini;
        _geminiOpt = geminiOpt.Value;
        _embedOpt = embedOpt.Value;
    }

    /// <summary>Query embedding via the configured provider (must match the chunk model).</summary>
    private Task<float[]?> EmbedQueryAsync(string query, CancellationToken ct) =>
        _embedOpt.Provider == "ollama" ? _ollama.EmbedAsync(query, ct) : _vertex.EmbedQueryAsync(query, ct);

    private string ActiveEmbedModel =>
        _embedOpt.Provider == "ollama" ? "nomic-embed-text" : _vertex.Model;

    public sealed record SearchRequest(
        string Query, string[]? SourceTypes, string[]? Tags, string? Jurisdiction,
        int? YearFrom, int? YearTo, int TopK = 8, bool Synthesize = false,
        double MinScore = 0);

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
        public string[]? Tags { get; set; }
        public string? ReviewId { get; set; }
        public int? CityMatterId { get; set; }
        public string? CityClient { get; set; }
    }

    /// <summary>Where a search result should send the user.</summary>
    private static object Link(Hit h) => h.SourceType switch
    {
        "study" => new { kind = "internal", href = $"/studies/{h.SourceKey}" },
        "federal_bill" when h.ReviewId is not null =>
            new { kind = "internal", href = $"/bills/{h.ReviewId}" },
        "federal_bill" => new { kind = "external", href = FederalUrl(h.SourceKey) },
        "city_matter" when h is { CityClient: not null, CityMatterId: not null } =>
            new { kind = "external", href = $"https://{h.CityClient}.legistar.com/gateway.aspx?m=l&id={h.CityMatterId}" },
        _ => new { kind = "external", href = "https://www.congress.gov" },
    };

    private static string FederalUrl(string sourceKey)
    {
        // '119-hr-6644' -> congress.gov bill URL
        var parts = sourceKey.Split('-');
        return parts.Length == 3 && int.TryParse(parts[0], out var congress) && int.TryParse(parts[2], out var number)
            ? TrackerRules.CongressGovUrl(congress, parts[1], number)
            : "https://www.congress.gov";
    }

    public async Task<object> SearchAsync(SearchRequest req, CancellationToken ct)
    {
        var topK = Math.Clamp(req.TopK, 1, 40);
        var vector = await EmbedQueryAsync(req.Query, ct);
        var (hits, mode) = vector is not null
            ? (await VectorSearchAsync(req, vector, topK), "vector")
            : (await KeywordSearchAsync(req, topK), "keyword");

        // Certainty floor applies to vector similarity only (keyword ts_rank
        // is on a different scale).
        if (mode == "vector" && req.MinScore > 0)
            hits = hits.Where(h => h.Score >= req.MinScore).ToList();

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

        // One row per document for result lists (best-scoring chunk wins,
        // order preserved); chunk-level hits still feed synthesis above.
        var documents = hits
            .GroupBy(h => h.DocumentId)
            .Select(g => new { Best = g.First(), ChunkMatches = g.Count() })
            .ToList();

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
            documents = documents.Select(x => new
            {
                document_id = x.Best.DocumentId,
                source_type = x.Best.SourceType,
                source_key = x.Best.SourceKey,
                title = x.Best.Title,
                jurisdiction = x.Best.Jurisdiction,
                doc_year = x.Best.DocYear,
                tags = x.Best.Tags ?? Array.Empty<string>(),
                snippet = x.Best.Content.Length > 320 ? x.Best.Content[..320] + "…" : x.Best.Content,
                score = Math.Round(x.Best.Score, 4),
                chunk_matches = x.ChunkMatches,
                link = Link(x.Best),
            }),
            answer,
            synthesis_error = synthesisError,
        };
    }

    private const string HitColumns = """
        c.chunk_id, d.document_id, d.source_type, d.source_key, d.title,
        d.jurisdiction, d.doc_year, c.content,
        ARRAY(SELECT t.name FROM document_tags dt JOIN tags t ON t.tag_id = dt.tag_id
              WHERE dt.document_id = d.document_id ORDER BY t.name) AS tags,
        br.review_id, cm.matter_id AS city_matter_id, cm.client AS city_client
        """;

    private const string HitJoins = """
        LEFT JOIN bill_reviews br ON d.source_type = 'federal_bill' AND br.bill_id = d.source_key
        LEFT JOIN city_matters cm ON d.source_type = 'city_matter' AND cm.city_matter_id = d.source_key
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
            {HitJoins}
            WHERE c.embedding IS NOT NULL AND c.embedding_model = @EmbedModel
              AND {DocumentRegistryService.PublishedOnly}
            """ + Filters(req, p) +
            " ORDER BY c.embedding <=> @Query::vector LIMIT @TopK";
        p.Add("TopK", topK);
        p.Add("EmbedModel", ActiveEmbedModel);
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
            {HitJoins}
            WHERE to_tsvector('english', c.content) @@ {fn}('english', @Q)
              AND {DocumentRegistryService.PublishedOnly}
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
