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
    private readonly StudiesOptions _opt;
    private readonly string _docsDir;

    private static readonly Regex RefPattern = new(@"^[A-Z]{2,10}-\d{4}-\d{1,6}$", RegexOptions.Compiled);

    public StudyService(DataLayerBase dl, DocumentRegistryService registry,
                        IOptions<StudiesOptions> options, IHostEnvironment env)
    {
        _dl = dl;
        _registry = registry;
        _opt = options.Value;
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

    public async Task<bool> ExistsAsync(string reference) =>
        await _dl.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM studies WHERE ref = @Ref)", new { Ref = reference });

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
}
