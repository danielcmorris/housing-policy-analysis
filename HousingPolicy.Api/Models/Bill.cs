namespace HousingPolicy.Api.Models;

/// <summary>
/// A single federal bill/law: normalized metadata plus its text versions.
/// This is both the DB projection (snake_case columns map in via Dapper) and
/// the JSON returned to the Angular front end. <see cref="TextVersions"/> is
/// populated by a second query, not by column mapping.
/// </summary>
public sealed class Bill
{
    public string BillId { get; set; } = "";
    public int Congress { get; set; }
    public string BillType { get; set; } = "";
    public int BillNumber { get; set; }
    public string? Title { get; set; }
    public string? OriginChamber { get; set; }
    public DateOnly? LatestActionDate { get; set; }
    public string? LatestActionText { get; set; }

    /// <summary>congress.gov updateDate — drives "is my copy stale?" for future sync.</summary>
    public DateTime? UpdateDate { get; set; }

    /// <summary>When WE retrieved it.</summary>
    public DateTime DataVintage { get; set; }

    public List<TextVersion> TextVersions { get; set; } = new();
}
