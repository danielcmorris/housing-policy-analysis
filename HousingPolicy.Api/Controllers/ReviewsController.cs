using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// The Center's authored bill reviews with live status merged from the
/// congressional record. Read-only; reviews are loaded by the Python seed
/// script (an outside script) or future authoring tooling.
/// </summary>
[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly ReviewService _reviews;

    public ReviewsController(ReviewService reviews) => _reviews = reviews;

    [HttpGet]
    public async Task<IActionResult> GetStore() => Ok(await _reviews.GetStoreAsync());

    [HttpGet("{reviewId}")]
    public async Task<IActionResult> GetReview(string reviewId)
    {
        var doc = await _reviews.GetReviewAsync(reviewId);
        return doc is null
            ? NotFound(new { detail = $"no review '{reviewId}'" })
            : Ok(doc);
    }
}
