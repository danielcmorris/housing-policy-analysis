using System.Text.Json.Nodes;
using HousingPolicy.Api.Modules;

namespace HousingPolicy.Api.Services;

/// <summary>
/// The Center's authored four-stage bill reviews, stored as JSONB (shape:
/// prototype/data/bill-review.schema.json) and served with live legislative
/// status merged in from `bills` — so the review page can never contradict
/// the tracker. Only status is live-merged; the editorial document
/// (provisions, precedents, projections, peer reviews) is returned verbatim.
/// </summary>
public sealed class ReviewService
{
    private readonly DataLayerBase _dl;

    public ReviewService(DataLayerBase dl) => _dl = dl;

    private sealed record ReviewRow(string ReviewId, string Review,
                                    string? LatestActionText, DateOnly? LatestActionDate);

    /// <summary>Overlay live status from the congressional record onto a review document.</summary>
    public static JsonNode MergeLiveStatus(JsonNode doc, string? latestActionText, DateOnly? latestActionDate)
    {
        var sk = TrackerRules.StatusKey(latestActionText);
        if (sk is not ("enacted" or "to_president" or "failed"))
            return doc; // editorial status (e.g. awaiting concurrence) is richer

        var meta = doc["meta"] as JsonObject ?? new JsonObject();
        doc["meta"] = meta.Parent is null ? meta : meta;
        meta["status"] = sk;
        var dateStr = latestActionDate?.ToString("yyyy-MM-dd");

        var stages = meta["legislativeStatus"] as JsonArray ?? new JsonArray();
        if (sk == "enacted")
        {
            foreach (var s in stages.OfType<JsonObject>())
                if ((string?)s["state"] == "in_progress")
                    s["state"] = "complete";
            if (!stages.OfType<JsonObject>().Any(s => (string?)s["stage"] == "enacted"))
                stages.Add(new JsonObject
                {
                    ["stage"] = "enacted", ["state"] = "complete", ["date"] = dateStr,
                });
            meta["legislativeStatus"] = stages.Parent is null ? stages : stages;
        }
        else if (sk == "to_president" &&
                 !stages.OfType<JsonObject>().Any(s => (string?)s["stage"] == "to_president"))
        {
            stages.Add(new JsonObject
            {
                ["stage"] = "to_president", ["state"] = "in_progress", ["date"] = dateStr,
            });
            meta["legislativeStatus"] = stages.Parent is null ? stages : stages;
        }
        return doc;
    }

    /// <summary>All reviews as the front-end store shape ({featuredBillId, bills{}}); featured follows the pinned bill.</summary>
    public async Task<JsonObject> GetStoreAsync()
    {
        var rows = (await _dl.QueryAsync<ReviewRow>(
            """
            SELECT r.review_id, r.review::text AS review, b.latest_action_text, b.latest_action_date
            FROM bill_reviews r
            LEFT JOIN bills b ON b.bill_id = r.bill_id
            ORDER BY COALESCE(b.pinned, FALSE) DESC, r.updated_at DESC
            """)).ToList();

        var bills = new JsonObject();
        foreach (var r in rows)
        {
            var doc = JsonNode.Parse(r.Review)!;
            bills[r.ReviewId] = MergeLiveStatus(doc, r.LatestActionText, r.LatestActionDate);
        }
        return new JsonObject
        {
            ["version"] = "1.0",
            ["defaultLocale"] = "en",
            ["featuredBillId"] = rows.Count > 0 ? rows[0].ReviewId : null,
            ["bills"] = bills,
        };
    }

    public async Task<JsonNode?> GetReviewAsync(string reviewId)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<ReviewRow>(
            """
            SELECT r.review_id, r.review::text AS review, b.latest_action_text, b.latest_action_date
            FROM bill_reviews r
            LEFT JOIN bills b ON b.bill_id = r.bill_id
            WHERE r.review_id = @ReviewId
            """, new { ReviewId = reviewId });
        if (row is null) return null;
        return MergeLiveStatus(JsonNode.Parse(row.Review)!, row.LatestActionText, row.LatestActionDate);
    }
}
