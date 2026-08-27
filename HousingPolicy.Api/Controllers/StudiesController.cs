using HousingPolicy.Api.Options;
using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Studies & policy proposals. Public reads serve displayed documents; the
/// admin add endpoint accepts a multipart form with the metadata, the
/// extracted document text (file or field), and the PDF itself. PDFs live on
/// local disk for now (bucket later) behind hard size limits.
/// </summary>
[ApiController]
public sealed class StudiesController : ControllerBase
{
    private readonly StudyService _studies;
    private readonly ExpertService _experts;
    private readonly StudiesOptions _opt;

    public StudiesController(StudyService studies, ExpertService experts, IOptions<StudiesOptions> options)
    {
        _studies = studies;
        _experts = experts;
        _opt = options.Value;
    }

    [HttpGet("api/studies")]
    public async Task<IActionResult> List(string view = "public", string? q = null, int limit = 100)
    {
        if (view is not ("public" or "admin"))
            return BadRequest(new { detail = "view must be 'public' or 'admin'" });
        return Ok(await _studies.ListAsync(view, q, Math.Clamp(limit, 1, 500)));
    }

    [HttpGet("api/studies/{reference}")]
    public async Task<IActionResult> Get(string reference)
    {
        var s = await _studies.GetAsync(reference);
        if (s is null) return NotFound(new { detail = $"no study '{reference}'" });
        return Ok(new { study = s, reviews = await _experts.ReviewsForStudyAsync(reference) });
    }

    [HttpGet("api/studies/{reference}/pdf")]
    public async Task<IActionResult> GetPdf(string reference)
    {
        var path = await _studies.GetPdfDiskPathAsync(reference);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound(new { detail = $"no PDF on file for '{reference}'" });
        return PhysicalFile(path, "application/pdf", $"{reference}.pdf");
    }

    public sealed class AddStudyForm
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
        /// <summary>One finding per line.</summary>
        public string? KeyFindings { get; set; }
        public string? Methodology { get; set; }
        /// <summary>Pasted document text (a text file upload wins over this).</summary>
        public string? TextContent { get; set; }
        public bool Display { get; set; } = true;
        public IFormFile? Pdf { get; set; }
        public IFormFile? TextFile { get; set; }
    }

    [HttpPost("api/admin/studies")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Add([FromForm] AddStudyForm form, CancellationToken ct)
    {
        var reference = form.Ref.Trim().ToUpperInvariant();
        if (!StudyService.IsValidRef(reference))
            return BadRequest(new { detail = "ref must look like CUHPR-2026-0142" });
        if (string.IsNullOrWhiteSpace(form.Title))
            return BadRequest(new { detail = "title is required" });
        if (form.DocType is not ("study" or "proposal"))
            return BadRequest(new { detail = "doc_type must be 'study' or 'proposal'" });
        if (await _studies.ExistsAsync(reference))
            return Conflict(new { detail = $"'{reference}' already exists" });

        if (form.Pdf is not null && form.Pdf.Length > _opt.MaxPdfBytes)
            return BadRequest(new { detail = $"PDF exceeds the {_opt.MaxPdfBytes / (1024 * 1024)} MB limit" });
        if (form.TextFile is not null && form.TextFile.Length > _opt.MaxTextBytes)
            return BadRequest(new { detail = $"text file exceeds the {_opt.MaxTextBytes / (1024 * 1024)} MB limit" });

        var text = form.TextContent;
        if (form.TextFile is not null)
        {
            using var reader = new StreamReader(form.TextFile.OpenReadStream());
            text = await reader.ReadToEndAsync(ct);
        }

        string? pdfPath = null;
        if (form.Pdf is not null)
        {
            await using var stream = form.Pdf.OpenReadStream();
            pdfPath = await _studies.SavePdfAsync(reference, stream, ct);
        }

        var findings = (form.KeyFindings ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        await _studies.InsertAsync(new StudyService.NewStudy(
            reference, form.DocType, form.Title.Trim(), form.Category, form.Authors,
            form.Year, form.Pages, form.Status, form.Clarity, form.Summary,
            findings, form.Methodology, text, form.Display), pdfPath);

        return Ok(new { @ref = reference, pdf_stored = pdfPath is not null,
                        text_stored = !string.IsNullOrEmpty(text), displayed = form.Display });
    }
}
