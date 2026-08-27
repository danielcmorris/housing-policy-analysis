using System.Text.RegularExpressions;
using HousingPolicy.Api.Modules;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Experts / reviewers: the vetted people who peer-review studies and bill
/// analyses. Public reads never expose email or internal notes. The lists of
/// studies and bills an expert has reviewed are derived from the
/// study_reviews / expert_bill_reviews join tables.
/// </summary>
public sealed class ExpertService
{
    private readonly DataLayerBase _dl;

    public ExpertService(DataLayerBase dl) => _dl = dl;

    public sealed class ExpertRow
    {
        public long ExpertId { get; set; }
        public string Slug { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Title { get; set; }
        public string? Affiliation { get; set; }
        public string? Category { get; set; }
        public string? Focus { get; set; }
        public string? Bio { get; set; }
        public string? Credentials { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? ProfileUrl { get; set; }
        public string? ScholarUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? ImageUrl { get; set; }
        public string? Location { get; set; }
        public string? Conflicts { get; set; }
        public bool Active { get; set; } = true;
        public DateOnly? JoinedAt { get; set; }
        public long StudyReviewCount { get; set; }
        public long BillReviewCount { get; set; }
    }

    public sealed class StudyReviewRow
    {
        public string StudyRef { get; set; } = "";
        public string? StudyTitle { get; set; }
        public string? Recommendation { get; set; }
        public decimal? Score { get; set; }
        public string? ReviewText { get; set; }
        public DateOnly? ReviewedAt { get; set; }
    }

    public sealed class BillReviewRow
    {
        public string ReviewId { get; set; } = "";
        public string? BillId { get; set; }
        public string? BillTitle { get; set; }
        public string? Recommendation { get; set; }
        public decimal? Score { get; set; }
        public string? ReviewText { get; set; }
        public DateOnly? ReviewedAt { get; set; }
    }

    public static string Slugify(string name) =>
        Regex.Replace(Regex.Replace(name.ToLowerInvariant().Normalize(), @"[^a-z0-9]+", "-"), "^-+|-+$", "");

    private const string PublicColumns = """
        e.expert_id, e.slug, e.full_name, e.title, e.affiliation, e.category, e.focus,
        e.bio, e.credentials, e.linkedin_url, e.profile_url, e.scholar_url, e.website_url,
        e.image_url, e.location, e.conflicts, e.active, e.joined_at,
        (SELECT count(*) FROM study_reviews sr WHERE sr.expert_id = e.expert_id AND sr.published) AS study_review_count,
        (SELECT count(*) FROM expert_bill_reviews br WHERE br.expert_id = e.expert_id AND br.published) AS bill_review_count
        """;

    public async Task<List<ExpertRow>> ListAsync(string? q, bool includeInactive)
    {
        var sql = $"SELECT {PublicColumns} FROM experts e WHERE TRUE";
        var p = new Dapper.DynamicParameters();
        if (!includeInactive) sql += " AND e.active";
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (e.full_name ILIKE @Like OR e.affiliation ILIKE @Like OR e.focus ILIKE @Like OR e.bio ILIKE @Like)";
            p.Add("Like", $"%{q.Trim()}%");
        }
        sql += " ORDER BY e.full_name";
        return (await _dl.QueryAsync<ExpertRow>(sql, p)).ToList();
    }

    public Task<ExpertRow?> GetAsync(string slug) =>
        _dl.QuerySingleOrDefaultAsync<ExpertRow>(
            $"SELECT {PublicColumns} FROM experts e WHERE e.slug = @Slug", new { Slug = slug });

    public async Task<List<StudyReviewRow>> StudyReviewsAsync(long expertId) =>
        (await _dl.QueryAsync<StudyReviewRow>(
            """
            SELECT sr.study_ref, s.title AS study_title, sr.recommendation, sr.score,
                   sr.review_text, sr.reviewed_at
            FROM study_reviews sr
            LEFT JOIN studies s ON s.ref = sr.study_ref
            WHERE sr.expert_id = @ExpertId AND sr.published
            ORDER BY sr.reviewed_at DESC NULLS LAST
            """, new { ExpertId = expertId })).ToList();

    public async Task<List<BillReviewRow>> BillReviewsAsync(long expertId) =>
        (await _dl.QueryAsync<BillReviewRow>(
            """
            SELECT br.review_id, r.bill_id, b.title AS bill_title, br.recommendation,
                   br.score, br.review_text, br.reviewed_at
            FROM expert_bill_reviews br
            LEFT JOIN bill_reviews r ON r.review_id = br.review_id
            LEFT JOIN bills b ON b.bill_id = r.bill_id
            WHERE br.expert_id = @ExpertId AND br.published
            ORDER BY br.reviewed_at DESC NULLS LAST
            """, new { ExpertId = expertId })).ToList();

    public sealed record UpsertExpert(
        string FullName, string? Title, string? Affiliation, string? Category, string? Focus,
        string? Bio, string? Credentials, string? LinkedinUrl, string? ProfileUrl,
        string? ScholarUrl, string? WebsiteUrl, string? ImageUrl, string? Email,
        string? Location, string? Conflicts, string? Notes, bool Active, string? Slug);

    /// <summary>Insert or update (by slug) an expert. Returns the slug.</summary>
    public async Task<string> UpsertAsync(UpsertExpert e)
    {
        var slug = string.IsNullOrWhiteSpace(e.Slug) ? Slugify(e.FullName) : e.Slug.Trim();
        await _dl.ExecuteAsync(
            """
            INSERT INTO experts (slug, full_name, title, affiliation, category, focus, bio,
                                 credentials, linkedin_url, profile_url, scholar_url,
                                 website_url, image_url, email, location, conflicts, notes,
                                 active, joined_at, created_at, updated_at)
            VALUES (@Slug, @FullName, @Title, @Affiliation, @Category, @Focus, @Bio,
                    @Credentials, @LinkedinUrl, @ProfileUrl, @ScholarUrl,
                    @WebsiteUrl, @ImageUrl, @Email, @Location, @Conflicts, @Notes,
                    @Active, CURRENT_DATE, now(), now())
            ON CONFLICT (slug) DO UPDATE SET
                full_name = EXCLUDED.full_name, title = EXCLUDED.title,
                affiliation = EXCLUDED.affiliation, category = EXCLUDED.category,
                focus = EXCLUDED.focus, bio = EXCLUDED.bio, credentials = EXCLUDED.credentials,
                linkedin_url = EXCLUDED.linkedin_url, profile_url = EXCLUDED.profile_url,
                scholar_url = EXCLUDED.scholar_url, website_url = EXCLUDED.website_url,
                image_url = EXCLUDED.image_url, email = EXCLUDED.email,
                location = EXCLUDED.location, conflicts = EXCLUDED.conflicts,
                notes = EXCLUDED.notes, active = EXCLUDED.active, updated_at = now()
            """,
            new
            {
                Slug = slug, e.FullName, e.Title, e.Affiliation, e.Category, e.Focus, e.Bio,
                e.Credentials, e.LinkedinUrl, e.ProfileUrl, e.ScholarUrl, e.WebsiteUrl,
                e.ImageUrl, e.Email, e.Location, e.Conflicts, e.Notes, e.Active,
            });
        return slug;
    }

    public sealed record RecordStudyReview(
        string StudyRef, string ExpertSlug, string? Recommendation, decimal? Score,
        string? ReviewText, DateOnly? ReviewedAt);

    public async Task<bool> AddStudyReviewAsync(RecordStudyReview r)
    {
        var updated = await _dl.ExecuteAsync(
            """
            INSERT INTO study_reviews (study_ref, expert_id, recommendation, score, review_text, reviewed_at)
            SELECT @StudyRef, e.expert_id, @Recommendation, @Score, @ReviewText,
                   COALESCE(@ReviewedAt, CURRENT_DATE)
            FROM experts e WHERE e.slug = @ExpertSlug
            ON CONFLICT (study_ref, expert_id) DO UPDATE SET
                recommendation = EXCLUDED.recommendation, score = EXCLUDED.score,
                review_text = EXCLUDED.review_text, reviewed_at = EXCLUDED.reviewed_at
            """, new { r.StudyRef, r.ExpertSlug, r.Recommendation, r.Score, r.ReviewText, r.ReviewedAt });
        return updated > 0;
    }

    public sealed record RecordBillReview(
        string ReviewId, string ExpertSlug, string? Recommendation, decimal? Score,
        string? ReviewText, DateOnly? ReviewedAt);

    public async Task<bool> AddBillReviewAsync(RecordBillReview r)
    {
        var updated = await _dl.ExecuteAsync(
            """
            INSERT INTO expert_bill_reviews (review_id, expert_id, recommendation, score, review_text, reviewed_at)
            SELECT @ReviewId, e.expert_id, @Recommendation, @Score, @ReviewText,
                   COALESCE(@ReviewedAt, CURRENT_DATE)
            FROM experts e WHERE e.slug = @ExpertSlug
            ON CONFLICT (review_id, expert_id) DO UPDATE SET
                recommendation = EXCLUDED.recommendation, score = EXCLUDED.score,
                review_text = EXCLUDED.review_text, reviewed_at = EXCLUDED.reviewed_at
            """, new { r.ReviewId, r.ExpertSlug, r.Recommendation, r.Score, r.ReviewText, r.ReviewedAt });
        return updated > 0;
    }

    /// <summary>Published peer reviews for one study, shaped for the study page.</summary>
    public async Task<List<object>> ReviewsForStudyAsync(string studyRef) =>
        (await _dl.QueryAsync<(string Slug, string FullName, string? Affiliation, string? Recommendation,
                               decimal? Score, string? ReviewText, DateOnly? ReviewedAt)>(
            """
            SELECT e.slug, e.full_name, e.affiliation, sr.recommendation, sr.score,
                   sr.review_text, sr.reviewed_at
            FROM study_reviews sr
            JOIN experts e ON e.expert_id = sr.expert_id
            WHERE sr.study_ref = @StudyRef AND sr.published
            ORDER BY sr.reviewed_at DESC NULLS LAST
            """, new { StudyRef = studyRef }))
        .Select(r => (object)new
        {
            expert_slug = r.Slug,
            name = r.FullName,
            affil = r.Affiliation,
            recommendation = r.Recommendation,
            score = r.Score,
            text = r.ReviewText,
            reviewed_at = r.ReviewedAt?.ToString("yyyy-MM-dd"),
        }).ToList();
}
