using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Embeds pending document chunks with the configured provider (Vertex
/// text-embedding-004). Runs are bounded (MaxChunksPerRun), scoped to a
/// single document when requested, and every run's exact token consumption
/// (reported by the embedding API itself) lands in the ai_usage ledger.
/// </summary>
public sealed class EmbeddingService
{
    private readonly DataLayerBase _dl;
    private readonly VertexEmbedClient _vertex;
    private readonly EmbeddingOptions _opt;

    public EmbeddingService(DataLayerBase dl, VertexEmbedClient vertex, IOptions<EmbeddingOptions> options)
    {
        _dl = dl;
        _vertex = vertex;
        _opt = options.Value;
    }

    private sealed class PendingChunk
    {
        public long ChunkId { get; set; }
        public string Content { get; set; } = "";
    }

    public async Task<object> EmbedPendingAsync(
        string? sourceType, string? sourceKey, int? limit, CancellationToken ct)
    {
        if (!_vertex.IsConfigured)
            throw new InvalidOperationException("Vertex embedding is not configured (no service-account key).");

        var cap = Math.Clamp(limit ?? _opt.MaxChunksPerRun, 1, _opt.MaxChunksPerRun);
        var sql = """
            SELECT c.chunk_id, c.content
            FROM document_chunks c
            JOIN documents d ON d.document_id = c.document_id
            WHERE c.embedding IS NULL
            """;
        var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(sourceType)) { sql += " AND d.source_type = @SourceType"; p.Add("SourceType", sourceType); }
        if (!string.IsNullOrEmpty(sourceKey)) { sql += " AND d.source_key = @SourceKey"; p.Add("SourceKey", sourceKey); }
        sql += " ORDER BY c.chunk_id LIMIT @Cap";
        p.Add("Cap", cap);

        var pending = (await _dl.QueryAsync<PendingChunk>(sql, p)).ToList();
        var embedded = 0;
        var totalTokens = 0;
        long totalChars = 0;
        var truncated = false;

        foreach (var batch in pending.Chunk(_opt.BatchSize))
        {
            var result = await _vertex.EmbedDocumentsAsync(batch.Select(b => b.Content).ToList(), ct);
            for (var i = 0; i < batch.Length; i++)
            {
                await _dl.ExecuteAsync(
                    """
                    UPDATE document_chunks
                    SET embedding = CAST(@Vec AS vector), embedding_model = @Model
                    WHERE chunk_id = @ChunkId
                    """,
                    new
                    {
                        Vec = "[" + string.Join(',', result.Vectors[i].Select(v => v.ToString("G7"))) + "]",
                        Model = _vertex.Model,
                        batch[i].ChunkId,
                    });
                totalChars += batch[i].Content.Length;
            }
            embedded += batch.Length;
            totalTokens += result.TotalTokens;
            truncated |= result.AnyTruncated;
        }

        if (embedded > 0)
            await _dl.ExecuteAsync(
                """
                INSERT INTO ai_usage (provider, model, purpose, input_tokens, output_tokens)
                VALUES ('vertex_embedding', @Model, 'chunk_embedding', @Tokens, 0)
                """,
                new { Model = _vertex.Model, Tokens = totalTokens });

        var remaining = await _dl.QuerySingleOrDefaultAsync<long>(
            "SELECT count(*) FROM document_chunks WHERE embedding IS NULL");

        return new
        {
            model = _vertex.Model,
            chunks_embedded = embedded,
            input_tokens = totalTokens,
            input_characters = totalChars,
            any_truncated = truncated,
            chunks_still_pending = remaining,
        };
    }
}
