namespace HousingPolicy.Api.Models;

// Sub-resource DTOs — double as Dapper read models (snake_case columns map via
// MatchNamesWithUnderscores) and the JSON returned to the Angular front end.

public sealed class Sponsor
{
    public string BioguideId { get; set; } = "";
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Party { get; set; }
    public string? State { get; set; }
    public int? District { get; set; }
    public string? IsByRequest { get; set; }
    public string? Url { get; set; }
}

public sealed class Cosponsor
{
    public string BioguideId { get; set; } = "";
    public string? FullName { get; set; }
    public string? Party { get; set; }
    public string? State { get; set; }
    public int? District { get; set; }
    public bool? IsOriginalCosponsor { get; set; }
    public DateOnly? SponsorshipDate { get; set; }
    public string? Url { get; set; }
}

public sealed class Amendment
{
    public int? AmendmentCongress { get; set; }
    public string AmendmentType { get; set; } = "";
    public string AmendmentNumber { get; set; } = "";
    public DateTime? UpdateDate { get; set; }
    public string? Url { get; set; }
}

public sealed class BillAction
{
    public int Ordinal { get; set; }
    public DateOnly? ActionDate { get; set; }
    public string? ActionCode { get; set; }
    public string? ActionType { get; set; }
    public int? SourceSystemCode { get; set; }
    public string? SourceSystemName { get; set; }
    public string? Text { get; set; }
}

public sealed class Committee
{
    public string SystemCode { get; set; } = "";
    public string? Chamber { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Activities { get; set; }   // JSON array text: [{date,name}, ...]
}

public sealed class Subject
{
    public string Name { get; set; } = "";
    public DateTime? UpdateDate { get; set; }
}

public sealed class Summary
{
    public string VersionCode { get; set; } = "";
    public DateOnly? ActionDate { get; set; }
    public string? ActionDesc { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? Text { get; set; }
}

public sealed class BillTitle
{
    public int Ordinal { get; set; }
    public string? Title { get; set; }
    public string? TitleType { get; set; }
    public int? TitleTypeCode { get; set; }
    public string? BillTextVersionCode { get; set; }
    public string? ChamberCode { get; set; }
    public string? ChamberName { get; set; }
    public DateTime? UpdateDate { get; set; }
}

public sealed class RelatedBill
{
    public int RelatedCongress { get; set; }
    public string RelatedType { get; set; } = "";
    public int RelatedNumber { get; set; }
    public string? Title { get; set; }
    public DateOnly? LatestActionDate { get; set; }
    public string? LatestActionText { get; set; }
    public string? RelationshipDetails { get; set; }   // JSON array text
    public string? Url { get; set; }
}
