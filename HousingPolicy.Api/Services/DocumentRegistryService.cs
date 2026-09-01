using Dapper;
using HousingPolicy.Api.Modules;

namespace HousingPolicy.Api.Services;

/// <summary>
/// The unified document registry + RAG layer. One `documents` row per logical
/// document across every corpus (federal bills, city matters, studies, and
/// future state bills), with canonical many-to-many tags (`tags` /
/// `document_tags`) and chunked text in `document_chunks` — the table the
/// vector-embedding pass will fill (embedding stays NULL until then).
///
/// A filtered RAG query joins documents (source_type / jurisdiction / year)
/// and document_tags BEFORE the vector distance, so the assistant can search
/// "city matters about Rent Regulation" or "federal bills, 2025+" cheaply.
/// </summary>
public sealed class DocumentRegistryService
{
    private readonly DataLayerBase _dl;

    /// <summary>Target chunk size (chars). ~1600 chars ≈ 400 tokens.</summary>
    private const int ChunkChars = 1600;
    private const int ChunkOverlap = 200;

    public DocumentRegistryService(DataLayerBase dl) => _dl = dl;

    /// <summary>
    /// SQL predicate (against a `documents d` alias) limiting rows to documents
    /// whose source record is published — display_date set and arrived, the
    /// same gate every public list uses. The registry itself indexes all
    /// content at sync time, so public retrieval (search, assistant) must
    /// apply this; admin-side tooling may query without it.
    /// </summary>
    public const string PublishedOnly = """
        CASE d.source_type
            WHEN 'federal_bill' THEN EXISTS (
                SELECT 1 FROM bills pb WHERE pb.bill_id = d.source_key
                AND pb.display_date IS NOT NULL AND pb.display_date <= now())
            WHEN 'city_matter' THEN EXISTS (
                SELECT 1 FROM city_matters pcm WHERE pcm.city_matter_id = d.source_key
                AND pcm.display_date IS NOT NULL AND pcm.display_date <= now())
            WHEN 'study' THEN EXISTS (
                SELECT 1 FROM studies ps WHERE ps.ref = d.source_key
                AND ps.display_date IS NOT NULL AND ps.display_date <= now())
            ELSE FALSE
        END
        """;

    // --- chunking ------------------------------------------------------------

    /// <summary>Split text into overlapping chunks on paragraph/sentence-friendly boundaries.</summary>
    public static List<string> Chunk(string text)
    {
        var chunks = new List<string>();
        var t = (text ?? "").Trim();
        if (t.Length == 0) return chunks;

        var pos = 0;
        while (pos < t.Length)
        {
            var end = Math.Min(pos + ChunkChars, t.Length);
            if (end < t.Length)
            {
                // prefer to break on a paragraph, then a sentence, then a space
                var window = t[pos..end];
                var cut = window.LastIndexOf("\n\n", StringComparison.Ordinal);
                if (cut < ChunkChars / 2) cut = window.LastIndexOf(". ", StringComparison.Ordinal);
                if (cut < ChunkChars / 2) cut = window.LastIndexOf(' ');
                if (cut > ChunkChars / 2) end = pos + cut + 1;
            }
            chunks.Add(t[pos..end].Trim());
            if (end >= t.Length) break;
            pos = Math.Max(end - ChunkOverlap, pos + 1);
        }
        return chunks.Where(c => c.Length > 0).ToList();
    }

    // --- registry upserts (called by the corpus services) --------------------

    public async Task UpsertFederalBillAsync(string billId, CancellationToken ct = default)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<(string? Title, int Congress, string[]? Tags, string? Text)>(
            """
            SELECT b.title, b.congress, b.tags,
                   (SELECT tv.text_content FROM bill_text_versions tv
                    WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL
                    ORDER BY tv.version_date DESC NULLS LAST LIMIT 1)
            FROM bills b WHERE b.bill_id = @BillId
            """, new { BillId = billId });
        if (row.Title is null && row.Text is null) return;
        await UpsertAsync("federal_bill", billId, row.Title, "US",
                          CongressStartYear(row.Congress),
                          row.Tags ?? Array.Empty<string>(), row.Text, ct);
    }

    /// <summary>First year of a Congress (119th -> 2025).</summary>
    private static int CongressStartYear(int congress) => 1789 + (congress - 1) * 2;

    public async Task UpsertCityMatterAsync(string cityMatterId, CancellationToken ct = default)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<(string? Title, string? CityName, DateOnly? IntroDate,
                                                       string[]? Tags, string? Text)>(
            """
            SELECT title, city_name, intro_date, tags, text_content
            FROM city_matters WHERE city_matter_id = @Id
            """, new { Id = cityMatterId });
        if (row.Title is null && row.Text is null) return;
        await UpsertAsync("city_matter", cityMatterId, row.Title, row.CityName,
                          row.IntroDate?.Year, row.Tags ?? Array.Empty<string>(), row.Text, ct);
    }

    /// <param name="includeText">When false, refresh only title/tags/metadata and
    /// leave the existing chunks (and their embeddings) untouched — used for
    /// metadata-only edits so a save doesn't force a re-embedding pass.</param>
    public async Task UpsertStudyAsync(string reference, bool includeText = true, CancellationToken ct = default)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<(string? Title, string? Authors, string? Category,
                                                       int? Year, string? Summary, string? Text)>(
            """
            SELECT title, authors, category, year, summary, text_content
            FROM studies WHERE ref = @Ref
            """, new { Ref = reference });
        if (row.Title is null) return;
        // Studies carry no tags column; derive from title + summary.
        var tags = TrackerRules.DeriveTags((row.Title ?? "") + " " + (row.Summary ?? ""));
        if (!string.IsNullOrEmpty(row.Category) && !tags.Contains(row.Category))
            tags = tags.Append(row.Category).ToArray();
        await UpsertAsync("study", reference, row.Title, row.Authors, row.Year, tags,
                          includeText ? row.Text : null, ct);
    }

    private async Task UpsertAsync(
        string sourceType, string sourceKey, string? title, string? jurisdiction,
        int? year, string[] tags, string? text, CancellationToken ct)
    {
        await using var con = await _dl.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        var documentId = await con.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT INTO documents (source_type, source_key, title, jurisdiction, doc_year, updated_at)
            VALUES (@SourceType, @SourceKey, @Title, @Jurisdiction, @Year, now())
            ON CONFLICT (source_type, source_key) DO UPDATE SET
                title = EXCLUDED.title, jurisdiction = EXCLUDED.jurisdiction,
                doc_year = EXCLUDED.doc_year, updated_at = now()
            RETURNING document_id
            """,
            new { SourceType = sourceType, SourceKey = sourceKey, Title = title,
                  Jurisdiction = jurisdiction, Year = year },
            transaction: tx, cancellationToken: ct));

        // Canonical tags: ensure tag rows, replace this document's links.
        await con.ExecuteAsync(new CommandDefinition(
            "DELETE FROM document_tags WHERE document_id = @Id", new { Id = documentId },
            transaction: tx, cancellationToken: ct));
        foreach (var tag in tags.Distinct(StringComparer.Ordinal))
        {
            await con.ExecuteAsync(new CommandDefinition(
                """
                WITH t AS (
                    INSERT INTO tags (name) VALUES (@Name)
                    ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
                    RETURNING tag_id
                )
                INSERT INTO document_tags (document_id, tag_id)
                SELECT @Id, tag_id FROM t
                ON CONFLICT DO NOTHING
                """, new { Name = tag, Id = documentId },
                transaction: tx, cancellationToken: ct));
        }

        // Chunks: replace wholesale when text is present (embeddings are a
        // later pass keyed on embedding IS NULL, so re-chunking resets them).
        if (!string.IsNullOrWhiteSpace(text))
        {
            await con.ExecuteAsync(new CommandDefinition(
                "DELETE FROM document_chunks WHERE document_id = @Id", new { Id = documentId },
                transaction: tx, cancellationToken: ct));
            var chunks = Chunk(text);
            var index = 0;
            foreach (var chunk in chunks)
            {
                await con.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO document_chunks (document_id, chunk_index, content, token_estimate)
                    VALUES (@Id, @Index, @Content, @Tokens)
                    """,
                    new { Id = documentId, Index = index++, Content = chunk, Tokens = chunk.Length / 4 },
                    transaction: tx, cancellationToken: ct));
            }
        }

        await tx.CommitAsync(ct);
    }

    // --- curated relations ---------------------------------------------------

    /// <summary>
    /// Seed document_relations from the structured sources we already hold:
    /// congress.gov related-bill data (federal↔federal) and the editorial
    /// precedentRefs inside bill_reviews (bill→study). Idempotent; 'manual'
    /// rows are never touched.
    /// </summary>
    public async Task<int> SeedRelationsAsync(CancellationToken ct = default)
    {
        var fromCongress = await _dl.ExecuteAsync(
            """
            INSERT INTO document_relations (from_document_id, to_document_id, relation, source)
            SELECT df.document_id, dt.document_id, 'related', 'congress_gov'
            FROM bill_related_bills rb
            JOIN documents df ON df.source_type = 'federal_bill' AND df.source_key = rb.bill_id
            JOIN documents dt ON dt.source_type = 'federal_bill'
                 AND dt.source_key = rb.related_congress || '-' || lower(rb.related_type) || '-' || rb.related_number
            WHERE df.document_id <> dt.document_id
            ON CONFLICT DO NOTHING
            """);

        var fromEditorial = await _dl.ExecuteAsync(
            """
            INSERT INTO document_relations (from_document_id, to_document_id, relation, source)
            SELECT db.document_id, ds.document_id, 'precedent', 'editorial'
            FROM bill_reviews br
            CROSS JOIN LATERAL jsonb_array_elements_text(br.review #> '{meta,precedentRefs}') AS pr(ref)
            JOIN documents db ON db.source_type = 'federal_bill' AND db.source_key = br.bill_id
            JOIN documents ds ON ds.source_type = 'study' AND ds.source_key = pr.ref
            WHERE jsonb_typeof(br.review #> '{meta,precedentRefs}') = 'array'
            ON CONFLICT DO NOTHING
            """);

        return fromCongress + fromEditorial;
    }

    // --- full rebuild --------------------------------------------------------

    /// <summary>Rebuild the registry from every corpus. Idempotent; safe to re-run.</summary>
    public async Task<object> RebuildAsync(CancellationToken ct = default)
    {
        var bills = (await _dl.QueryAsync<string>("SELECT bill_id FROM bills")).ToList();
        foreach (var b in bills) await UpsertFederalBillAsync(b, ct);

        var matters = (await _dl.QueryAsync<string>("SELECT city_matter_id FROM city_matters")).ToList();
        foreach (var m in matters) await UpsertCityMatterAsync(m, ct);

        var studies = (await _dl.QueryAsync<string>("SELECT ref FROM studies")).ToList();
        foreach (var s in studies) await UpsertStudyAsync(s, includeText: true, ct);

        var newRelations = await SeedRelationsAsync(ct);

        var stats = await _dl.QuerySingleOrDefaultAsync<(long Docs, long Chunks, long Tags, long Links, long Relations)>(
            """
            SELECT (SELECT count(*) FROM documents),
                   (SELECT count(*) FROM document_chunks),
                   (SELECT count(*) FROM tags),
                   (SELECT count(*) FROM document_tags),
                   (SELECT count(*) FROM document_relations)
            """);
        return new
        {
            federal_bills = bills.Count, city_matters = matters.Count, studies = studies.Count,
            documents = stats.Docs, chunks = stats.Chunks, tags = stats.Tags, tag_links = stats.Links,
            relations = stats.Relations, relations_added = newRelations,
        };
    }

    public async Task<object> StatsAsync()
    {
        var rows = (await _dl.QueryAsync<(string SourceType, long Docs, long Chunks)>(
            """
            SELECT d.source_type, count(DISTINCT d.document_id),
                   count(c.chunk_id)
            FROM documents d
            LEFT JOIN document_chunks c ON c.document_id = d.document_id
            GROUP BY d.source_type ORDER BY d.source_type
            """)).ToList();
        var pending = await _dl.QuerySingleOrDefaultAsync<long>(
            "SELECT count(*) FROM document_chunks WHERE embedding IS NULL");
        return new
        {
            by_source = rows.Select(r => new { source_type = r.SourceType, documents = r.Docs, chunks = r.Chunks }),
            chunks_awaiting_embedding = pending,
        };
    }
}
