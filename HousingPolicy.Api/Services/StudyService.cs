using System.Text.RegularExpressions;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Studies and policy proposals. These are added manually (no upstream API):
/// metadata + summary + extracted text go to the `studies` table, and the PDF
/// is written to local disk under Studies:DocumentsDir (object storage later).
/// Public visibility follows the bills rule: display_date set and arrived.
/// </summary>
public sealed class StudyService
{
    private readonly DataLayerBase _dl;
    private readonly DocumentRegistryService _registry;
    private readonly GeminiClient _gemini;
    private readonly StudiesOptions _opt;
    private readonly GeminiOptions _geminiOpt;
    private readonly string _docsDir;

    private static readonly Regex RefPattern = new(@"^[A-Z]{2,10}-\d{4}-\d{1,6}$", RegexOptions.Compiled);

    public StudyService(DataLayerBase dl, DocumentRegistryService registry, GeminiClient gemini,
                        IOptions<StudiesOptions> options, IOptions<GeminiOptions> geminiOptions,
                        IHostEnvironment env)
    {
        _dl = dl;
        _registry = registry;
        _gemini = gemini;
        _opt = options.Value;
        _geminiOpt = geminiOptions.Value;
        _docsDir = Path.IsPathRooted(_opt.DocumentsDir)
            ? _opt.DocumentsDir
            : Path.Combine(env.ContentRootPath, _opt.DocumentsDir);
    }

    public sealed class StudyRow
    {
        public string Ref { get; set; } = "";
        public string DocType { get; set; } = "study";
        public string Title { get; set; } = "";
        public string? Category { get; set; }
        public string? Authors { get; set; }
        public int? Year { get; set; }
        public int? Pages { get; set; }
        public string Status { get; set; } = "Submitted";
        public decimal? Clarity { get; set; }
        public string? Summary { get; set; }
        public string[] KeyFindings { get; set; } = Array.Empty<string>();
        public string? Methodology { get; set; }
        public string? PdfPath { get; set; }
        public DateTime? DisplayDate { get; set; }
        public bool Pinned { get; set; }
        public bool HasText { get; set; }
        public bool HasPdf => !string.IsNullOrEmpty(PdfPath);
        public bool Displayed => DisplayDate is not null && DisplayDate <= DateTime.UtcNow;
    }

    public sealed record NewStudy(
        string Ref, string DocType, string Title, string? Category, string? Authors,
        int? Year, int? Pages, string Status, decimal? Clarity, string? Summary,
        string[] KeyFindings, string? Methodology, string? TextContent, bool Display);

    /// <summary>Validate a study ref ('CUHPR-2026-0142' shape) — it names files and routes.</summary>
    public static bool IsValidRef(string reference) => RefPattern.IsMatch(reference);

    public async Task<List<StudyRow>> ListAsync(string view, string? q, int limit)
    {
        var sql = """
            SELECT ref, doc_type, title, category, authors, year, pages, status, clarity,
                   summary, key_findings, methodology, pdf_path, display_date, pinned,
                   (text_content IS NOT NULL) AS has_text
            FROM studies
            WHERE TRUE
            """;
        var p = new Dapper.DynamicParameters();
        if (view == "public")
            sql += " AND display_date IS NOT NULL AND display_date <= now()";
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (title ILIKE @Like OR authors ILIKE @Like OR summary ILIKE @Like OR ref ILIKE @Like)";
            p.Add("Like", $"%{q.Trim()}%");
        }
        sql += " ORDER BY pinned DESC, year DESC NULLS LAST, ref DESC LIMIT @Limit";
        p.Add("Limit", limit);
        return (await _dl.QueryAsync<StudyRow>(sql, p)).ToList();
    }

    public Task<StudyRow?> GetAsync(string reference) =>
        _dl.QuerySingleOrDefaultAsync<StudyRow>(
            """
            SELECT ref, doc_type, title, category, authors, year, pages, status, clarity,
                   summary, key_findings, methodology, pdf_path, display_date, pinned,
                   (text_content IS NOT NULL) AS has_text
            FROM studies WHERE ref = @Ref
            """, new { Ref = reference });

    public Task<string?> GetPdfDiskPathAsync(string reference) =>
        _dl.QuerySingleOrDefaultAsync<string>(
            "SELECT pdf_path FROM studies WHERE ref = @Ref", new { Ref = reference })
        .ContinueWith(t => t.Result is { } rel ? Path.Combine(_docsDir, rel) : null);

    public sealed class AdminStudyDetail
    {
        public StudyRow Study { get; set; } = new();
        public string? TextContent { get; set; }
        public long Chunks { get; set; }
        public long ChunksPending { get; set; }
    }

    /// <summary>Full admin view of one study: every field including the document
    /// text, plus this document's chunk/embedding counts from the registry.</summary>
    public async Task<AdminStudyDetail?> AdminGetAsync(string reference)
    {
        var row = await GetAsync(reference);
        if (row is null) return null;
        var text = await _dl.QuerySingleOrDefaultAsync<string?>(
            "SELECT text_content FROM studies WHERE ref = @Ref", new { Ref = reference });
        var stats = await _dl.QuerySingleOrDefaultAsync<(long Chunks, long Pending)>(
            """
            SELECT count(*), count(*) FILTER (WHERE c.embedding IS NULL)
            FROM document_chunks c
            JOIN documents d ON d.document_id = c.document_id
            WHERE d.source_type = 'study' AND d.source_key = @Ref
            """, new { Ref = reference });
        return new AdminStudyDetail
        {
            Study = row, TextContent = text,
            Chunks = stats.Chunks, ChunksPending = stats.Pending,
        };
    }

    public async Task<bool> ExistsAsync(string reference) =>
        await _dl.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM studies WHERE ref = @Ref)", new { Ref = reference });

    /// <summary>
    /// Extract the plain text of a stored PDF with PdfPig (content-order pass,
    /// local and free — no cloud calls). Works for born-digital PDFs only; a
    /// scanned/image PDF yields little or no text and would need OCR instead.
    /// </summary>
    public static (string Text, int Pages) ExtractPdfText(string path)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new System.Text.StringBuilder();
        var pages = 0;
        foreach (var page in doc.GetPages())
        {
            pages++;
            var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
                .ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);
            if (!string.IsNullOrWhiteSpace(text))
                sb.Append(text.Trim()).Append("\n\n");
        }
        return (sb.ToString().Trim(), pages);
    }

    public sealed record MarkdownResult(string Text, int Pages, int InputTokens, int OutputTokens, string Model);

    /// <summary>
    /// Convert a stored PDF to Markdown via Gemini (native PDF input). The
    /// pre-call assessment is the page count: Vertex bills PDFs flat at ~258
    /// tokens/page, so pages beyond PdfMarkdownMaxPages are refused before any
    /// call is made; the output cap scales with pages. Usage lands in ai_usage.
    /// </summary>
    public async Task<MarkdownResult> ConvertPdfToMarkdownAsync(string pdfPath, CancellationToken ct)
    {
        int pages;
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
            pages = doc.NumberOfPages;
        if (pages > _geminiOpt.PdfMarkdownMaxPages)
            throw new InvalidOperationException(
                $"PDF has {pages} pages — the markdown-conversion cap is {_geminiOpt.PdfMarkdownMaxPages} pages");

        var maxOut = Math.Min(pages * _geminiOpt.PdfMarkdownOutputTokensPerPage,
                              _geminiOpt.PdfMarkdownMaxOutputTokens);
        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var result = await _gemini.ConvertPdfToMarkdownAsync(bytes, maxOut, ct);
        if (string.IsNullOrWhiteSpace(result.Text))
            throw new InvalidOperationException("Gemini returned no text for this PDF");
        var markdown = SanitizeMarkdown(result.Text);

        await _dl.ExecuteAsync(
            """
            INSERT INTO ai_usage (provider, model, purpose, input_tokens, output_tokens)
            VALUES ('vertex_gemini', @Model, 'pdf_markdown', @In, @Out)
            """,
            new { Model = _geminiOpt.PdfMarkdownModel, In = result.InputTokens, Out = result.OutputTokens });

        return new MarkdownResult(markdown, pages, result.InputTokens, result.OutputTokens,
                                  _geminiOpt.PdfMarkdownModel);
    }

    /// <summary>
    /// Deterministic cleanup of model transcription artifacts: TOC dot leaders
    /// and similar filler-character runs (....., -----, ····) that the model
    /// can copy from the PDF and then loop on, and 3+ blank lines. Horizontal
    /// rules survive: a line that is exactly '---' is left alone.
    /// </summary>
    public static string SanitizeMarkdown(string text)
    {
        text = Regex.Replace(text, @"(?<![\r\n])([.\-_·•⋅…])\1{3,}", " ");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{4,}", "\n\n\n");
        return text.Trim();
    }

    /// <summary>Save the uploaded PDF under the documents dir; returns the stored relative path.</summary>
    public async Task<string> SavePdfAsync(string reference, Stream pdf, CancellationToken ct)
    {
        var rel = Path.Combine("studies", $"{reference}.pdf");
        var full = Path.Combine(_docsDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await pdf.CopyToAsync(file, ct);
        return rel;
    }

    public async Task InsertAsync(NewStudy s, string? pdfPath)
    {
        await _dl.ExecuteAsync(
            """
            INSERT INTO studies (ref, doc_type, title, category, authors, year, pages, status,
                                 clarity, summary, key_findings, methodology, text_content,
                                 pdf_path, display_date, pinned, created_at, updated_at)
            VALUES (@Ref, @DocType, @Title, @Category, @Authors, @Year, @Pages, @Status,
                    @Clarity, @Summary, @KeyFindings, @Methodology, @TextContent,
                    @PdfPath, CASE WHEN @Display THEN now() ELSE NULL END, FALSE, now(), now())
            """,
            new
            {
                s.Ref, s.DocType, s.Title, s.Category, s.Authors, s.Year, s.Pages, s.Status,
                s.Clarity, s.Summary, s.KeyFindings, s.Methodology, s.TextContent,
                PdfPath = pdfPath, s.Display,
            });
        await _registry.UpsertStudyAsync(s.Ref);
    }

    /// <summary>
    /// Update every editable property of a study. The document text is only
    /// replaced when <paramref name="replaceText"/> is set (a blank edit box must
    /// not wipe stored text); the PDF path only when a new PDF was uploaded.
    /// Returns whether the registry re-chunked (i.e. embeddings need a new pass).
    /// </summary>
    public async Task<bool> UpdateAsync(NewStudy s, string? pdfPath, bool replaceText)
    {
        var textChanged = replaceText && await _dl.QuerySingleOrDefaultAsync<bool>(
            "SELECT text_content IS DISTINCT FROM @Text FROM studies WHERE ref = @Ref",
            new { Text = s.TextContent, s.Ref });
        await _dl.ExecuteAsync(
            """
            UPDATE studies SET
                doc_type = @DocType, title = @Title, category = @Category, authors = @Authors,
                year = @Year, pages = @Pages, status = @Status, clarity = @Clarity,
                summary = @Summary, key_findings = @KeyFindings, methodology = @Methodology,
                text_content = CASE WHEN @ReplaceText THEN @TextContent ELSE text_content END,
                pdf_path = COALESCE(@PdfPath, pdf_path),
                display_date = CASE WHEN @Display THEN COALESCE(display_date, now()) ELSE NULL END,
                updated_at = now()
            WHERE ref = @Ref
            """,
            new
            {
                s.Ref, s.DocType, s.Title, s.Category, s.Authors, s.Year, s.Pages, s.Status,
                s.Clarity, s.Summary, s.KeyFindings, s.Methodology, s.TextContent,
                ReplaceText = replaceText, PdfPath = pdfPath, s.Display,
            });
        // Metadata-only saves keep the existing chunks (and embeddings) intact.
        await _registry.UpsertStudyAsync(s.Ref, includeText: textChanged);
        return textChanged;
    }

    public Task<int> SetDisplayAsync(string reference, bool displayed) =>
        _dl.ExecuteAsync(
            """
            UPDATE studies
            SET display_date = CASE WHEN @Displayed THEN now() ELSE NULL END, updated_at = now()
            WHERE ref = @Ref
            """, new { Ref = reference, Displayed = displayed });

    public Task<int> SetPinnedAsync(string reference, bool pinned) =>
        _dl.ExecuteAsync(
            "UPDATE studies SET pinned = @Pinned, updated_at = now() WHERE ref = @Ref",
            new { Ref = reference, Pinned = pinned });
}
