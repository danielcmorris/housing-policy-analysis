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
        /// <summary>Update only: replace the stored text with TextContent (a
        /// text file upload implies this). Off by default so a metadata-only
        /// save can never wipe the stored document text.</summary>
        public bool ReplaceText { get; set; }
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

    /// <summary>Full admin view of one study, including the document text and
    /// its chunk/embedding counts, for the edit page.</summary>
    [HttpGet("api/admin/studies/{reference}")]
    public async Task<IActionResult> AdminGet(string reference)
    {
        var detail = await _studies.AdminGetAsync(reference.ToUpperInvariant());
        if (detail is null) return NotFound(new { detail = $"no study '{reference}'" });
        return Ok(new
        {
            study = detail.Study,
            text_content = detail.TextContent,
            chunks = detail.Chunks,
            chunks_pending = detail.ChunksPending,
        });
    }

    /// <summary>
    /// Update a study: metadata, display flag, optionally a replacement PDF and
    /// replacement document text. Text is only replaced when a text file is
    /// uploaded or ReplaceText is set (so a metadata-only save can't wipe it);
    /// a text change re-chunks the registry, which queues a new embedding pass.
    /// </summary>
    [HttpPut("api/admin/studies/{reference}")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Update(string reference, [FromForm] AddStudyForm form,
                                            CancellationToken ct)
    {
        var replaceText = form.ReplaceText;
        reference = reference.Trim().ToUpperInvariant();
        if (!await _studies.ExistsAsync(reference))
            return NotFound(new { detail = $"no study '{reference}'" });
        if (string.IsNullOrWhiteSpace(form.Title))
            return BadRequest(new { detail = "title is required" });
        if (form.DocType is not ("study" or "proposal"))
            return BadRequest(new { detail = "doc_type must be 'study' or 'proposal'" });
        if (form.Pdf is not null && form.Pdf.Length > _opt.MaxPdfBytes)
            return BadRequest(new { detail = $"PDF exceeds the {_opt.MaxPdfBytes / (1024 * 1024)} MB limit" });
        if (form.TextFile is not null && form.TextFile.Length > _opt.MaxTextBytes)
            return BadRequest(new { detail = $"text file exceeds the {_opt.MaxTextBytes / (1024 * 1024)} MB limit" });

        var text = form.TextContent;
        if (form.TextFile is not null)
        {
            using var reader = new StreamReader(form.TextFile.OpenReadStream());
            text = await reader.ReadToEndAsync(ct);
            replaceText = true;
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

        var rechunked = await _studies.UpdateAsync(new StudyService.NewStudy(
            reference, form.DocType, form.Title.Trim(), form.Category, form.Authors,
            form.Year, form.Pages, form.Status, form.Clarity, form.Summary,
            findings, form.Methodology, text, form.Display), pdfPath, replaceText);

        return Ok(new { @ref = reference, pdf_replaced = pdfPath is not null,
                        text_replaced = replaceText, rechunked, displayed = form.Display });
    }

    /// <summary>
    /// Parse the stored PDF into plain text with PdfPig (runs locally, no cloud
    /// call) and return it for review — nothing is saved; the client places the
    /// text in the editor and the admin saves it explicitly.
    /// </summary>
    [HttpPost("api/admin/studies/{reference}/parse-pdf")]
    public async Task<IActionResult> ParsePdf(string reference)
    {
        reference = reference.ToUpperInvariant();
        var path = await _studies.GetPdfDiskPathAsync(reference);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound(new { detail = $"no PDF on file for '{reference}'" });
        try
        {
            var (text, pages) = await Task.Run(() => StudyService.ExtractPdfText(path));
            if (string.IsNullOrWhiteSpace(text))
                return UnprocessableEntity(new { detail =
                    "the PDF contains no extractable text — it is likely a scanned/image PDF, which needs OCR" });
            return Ok(new { @ref = reference, pages, characters = text.Length, text });
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { detail = $"could not parse the PDF: {ex.Message}" });
        }
    }

    /// <summary>
    /// Convert the stored PDF to Markdown via Gemini (this DOES call Vertex AI;
    /// the flash-lite model, page/output caps, and ai_usage logging keep the
    /// cost bounded — ~2¢ per document worst case). Nothing is saved; the
    /// client places the markdown in the editor and the admin saves explicitly.
    /// </summary>
    [HttpPost("api/admin/studies/{reference}/convert-markdown")]
    public async Task<IActionResult> ConvertMarkdown(string reference, CancellationToken ct)
    {
        reference = reference.ToUpperInvariant();
        var path = await _studies.GetPdfDiskPathAsync(reference);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound(new { detail = $"no PDF on file for '{reference}'" });
        try
        {
            var r = await _studies.ConvertPdfToMarkdownAsync(path, ct);
            return Ok(new { @ref = reference, r.Pages, characters = r.Text.Length,
                            input_tokens = r.InputTokens, output_tokens = r.OutputTokens,
                            model = r.Model, text = r.Text });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { detail = ex.Message });
        }
    }

    public sealed record DisplayRequest(bool Displayed);
    public sealed record PinRequest(bool Pinned);

    [HttpPost("api/admin/studies/{reference}/display")]
    public async Task<IActionResult> SetDisplay(string reference, [FromBody] DisplayRequest body)
    {
        reference = reference.ToUpperInvariant();
        if (await _studies.SetDisplayAsync(reference, body.Displayed) == 0)
            return NotFound(new { detail = $"no study '{reference}'" });
        var row = await _studies.GetAsync(reference);
        return Ok(new { @ref = reference, display_date = row!.DisplayDate, displayed = row.Displayed });
    }

    [HttpPost("api/admin/studies/{reference}/pin")]
    public async Task<IActionResult> SetPin(string reference, [FromBody] PinRequest body)
    {
        reference = reference.ToUpperInvariant();
        if (await _studies.SetPinnedAsync(reference, body.Pinned) == 0)
            return NotFound(new { detail = $"no study '{reference}'" });
        return Ok(new { @ref = reference, pinned = body.Pinned });
    }
}
