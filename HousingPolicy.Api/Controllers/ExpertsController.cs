using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// Experts / reviewers. Public reads (roster + profile with reviewed studies
/// and bills); admin writes (upsert an expert, record a study or bill review).
/// Email and internal notes are accepted on write but never returned by reads.
/// </summary>
[ApiController]
public sealed class ExpertsController : ControllerBase
{
    private readonly ExpertService _experts;

    public ExpertsController(ExpertService experts) => _experts = experts;

    [HttpGet("api/experts")]
    public async Task<IActionResult> List(string? q = null, bool include_inactive = false) =>
        Ok(await _experts.ListAsync(q, include_inactive));

    [HttpGet("api/experts/{slug}")]
    public async Task<IActionResult> Get(string slug)
    {
        var e = await _experts.GetAsync(slug);
        if (e is null) return NotFound(new { detail = $"no expert '{slug}'" });
        return Ok(new
        {
            expert = e,
            study_reviews = await _experts.StudyReviewsAsync(e.ExpertId),
            bill_reviews = await _experts.BillReviewsAsync(e.ExpertId),
        });
    }

    public sealed record ExpertForm(
        string FullName, string? Title, string? Affiliation, string? Category, string? Focus,
        string? Bio, string? Credentials, string? LinkedinUrl, string? ProfileUrl,
        string? ScholarUrl, string? WebsiteUrl, string? ImageUrl, string? Email,
        string? Location, string? Conflicts, string? Notes, bool Active = true, string? Slug = null);

    [HttpPost("api/admin/experts")]
    public async Task<IActionResult> Upsert([FromBody] ExpertForm form)
    {
        if (string.IsNullOrWhiteSpace(form.FullName))
            return BadRequest(new { detail = "full_name is required" });
        var slug = await _experts.UpsertAsync(new ExpertService.UpsertExpert(
            form.FullName.Trim(), form.Title, form.Affiliation, form.Category, form.Focus,
            form.Bio, form.Credentials, form.LinkedinUrl, form.ProfileUrl, form.ScholarUrl,
            form.WebsiteUrl, form.ImageUrl, form.Email, form.Location, form.Conflicts,
            form.Notes, form.Active, form.Slug));
        return Ok(new { slug });
    }

    public sealed record StudyReviewForm(
        string StudyRef, string ExpertSlug, string? Recommendation, decimal? Score,
        string? ReviewText, DateOnly? ReviewedAt);

    [HttpPost("api/admin/study-reviews")]
    public async Task<IActionResult> AddStudyReview([FromBody] StudyReviewForm form)
    {
        var ok = await _experts.AddStudyReviewAsync(new ExpertService.RecordStudyReview(
            form.StudyRef, form.ExpertSlug, form.Recommendation, form.Score,
            form.ReviewText, form.ReviewedAt));
        return ok ? Ok(new { recorded = true })
                  : NotFound(new { detail = $"unknown expert '{form.ExpertSlug}' (or study ref)" });
    }

    public sealed record BillReviewForm(
        string ReviewId, string ExpertSlug, string? Recommendation, decimal? Score,
        string? ReviewText, DateOnly? ReviewedAt);

    [HttpPost("api/admin/bill-reviews")]
    public async Task<IActionResult> AddBillReview([FromBody] BillReviewForm form)
    {
        var ok = await _experts.AddBillReviewAsync(new ExpertService.RecordBillReview(
            form.ReviewId, form.ExpertSlug, form.Recommendation, form.Score,
            form.ReviewText, form.ReviewedAt));
        return ok ? Ok(new { recorded = true })
                  : NotFound(new { detail = $"unknown expert '{form.ExpertSlug}' (or review id)" });
    }
}
