using System.Text.RegularExpressions;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Pure tracker logic (port of the Python legislation.py rule layer): coarse
/// legislative-stage classification, deterministic keyword tagging of CRS
/// summaries, watch-flag derivation, and display formatting. No I/O.
/// </summary>
public static class TrackerRules
{
    /// <summary>The CRS policy-area term that defines the tracker corpus.</summary>
    public const string HousingPolicyArea = "Housing and Community Development";

    // --- stage classification ------------------------------------------------

    /// <summary>Coarse pipeline stage for a status pill, from the latest-action text.</summary>
    public static string StatusKey(string? latestActionText)
    {
        var t = (latestActionText ?? "").ToLowerInvariant();
        if (t.Contains("became public law") || t.Contains("became private law")) return "enacted";
        if (t.Contains("vetoed") && !t.Contains("overridden")) return "failed";
        if (t.Contains("to the president") || t.Contains("presented to president")) return "to_president";
        if (t.Contains("passed") || t.Contains("agreed to") || t.Contains("calendar") ||
            t.Contains("reported") || t.Contains("ordered to be reported") ||
            t.Contains("motion to reconsider") || t.Contains("amendment")) return "advancing";
        if (t.Contains("committee") || t.Contains("read twice") || t.Contains("referred") ||
            t.Contains("hearing")) return "committee";
        return "introduced";
    }

    // --- summary tagging -----------------------------------------------------
    // Keyword taxonomy scanned against title + CRS summary when the summary is
    // first ingested. Deterministic and local — no model calls. Short keywords
    // (<= 5 chars) match on word boundaries so 'PHA' doesn't hit 'alpha'.

    private static readonly (string Tag, string[] Keywords)[] TagRules =
    {
        ("Zoning & Land Use", new[] { "zoning", "land use", "land-use", "upzoning", "lot size",
                                      "density", "permitting", "by-right", "entitlement" }),
        ("Housing Supply", new[] { "housing supply", "housing production", "new construction",
                                   "housing shortage", "supply of housing", "increase the supply",
                                   "housing units", "infill" }),
        ("Affordable Housing", new[] { "affordable housing", "affordability", "low-income housing",
                                       "workforce housing", "moderate-income" }),
        ("Rent Regulation", new[] { "rent control", "rent stabilization", "rent cap", "rent regulation" }),
        ("Tenant Protections", new[] { "tenant", "eviction", "just cause", "renter" }),
        ("Public Housing", new[] { "public housing", "housing agency", "pha" }),
        ("Vouchers & Rental Assistance", new[] { "voucher", "section 8", "rental assistance",
                                                 "housing choice" }),
        ("Homelessness", new[] { "homeless", "emergency shelter", "supportive housing" }),
        ("Mortgage & Finance", new[] { "mortgage", "fha", "loan", "lender", "underwriting",
                                       "housing finance", "appraisal" }),
        ("Tax Credits & Incentives", new[] { "tax credit", "lihtc", "opportunity zone", "tax incentive" }),
        ("Manufactured & Modular", new[] { "manufactured hous", "manufactured home", "modular",
                                           "factory-built" }),
        ("Rural Housing", new[] { "rural housing", "rural development", "usda" }),
        ("Veterans Housing", new[] { "veteran" }),
        ("Environmental Review", new[] { "environmental review", "nepa" }),
        ("Homeownership", new[] { "homeowner", "homeownership", "first-time homebuyer", "down payment" }),
        ("Disaster Recovery", new[] { "disaster", "resilience" }),
        ("Community Development", new[] { "community development", "cdbg", "block grant" }),
    };

    /// <summary>Core housing-policy mechanisms the Center studies; any of these marks a bill worth watching.</summary>
    private static readonly HashSet<string> WatchTags = new(StringComparer.Ordinal)
    {
        "Zoning & Land Use", "Housing Supply", "Affordable Housing", "Rent Regulation",
        "Tenant Protections", "Public Housing", "Vouchers & Rental Assistance", "Homelessness",
    };

    public const int MaxTags = 4;

    /// <summary>A few topic tags for a bill, scored by keyword hits in title+summary.</summary>
    public static string[] DeriveTags(string text)
    {
        var t = (text ?? "").ToLowerInvariant();
        var scored = new List<(int Hits, string Tag)>();
        foreach (var (tag, keywords) in TagRules)
        {
            var hits = 0;
            foreach (var kw in keywords)
            {
                if (kw.Length <= 5)
                    hits += Regex.Matches(t, $@"\b{Regex.Escape(kw)}\b").Count;
                else
                {
                    var idx = 0;
                    while ((idx = t.IndexOf(kw, idx, StringComparison.Ordinal)) >= 0) { hits++; idx += kw.Length; }
                }
            }
            if (hits > 0) scored.Add((hits, tag));
        }
        return scored
            .OrderByDescending(s => s.Hits).ThenBy(s => s.Tag, StringComparer.Ordinal)
            .Take(MaxTags).Select(s => s.Tag).ToArray();
    }

    public static bool IsWatch(IEnumerable<string>? tags) =>
        tags is not null && tags.Any(WatchTags.Contains);

    // --- display formatting --------------------------------------------------

    private static readonly Dictionary<string, string> RefPrefix = new(StringComparer.Ordinal)
    {
        ["hr"] = "H.R.", ["s"] = "S.", ["hjres"] = "H.J.Res.", ["sjres"] = "S.J.Res.",
        ["hconres"] = "H.Con.Res.", ["sconres"] = "S.Con.Res.", ["hres"] = "H.Res.", ["sres"] = "S.Res.",
    };

    private static readonly Dictionary<string, string> UrlSegment = new(StringComparer.Ordinal)
    {
        ["hr"] = "house-bill", ["s"] = "senate-bill", ["hres"] = "house-resolution",
        ["sres"] = "senate-resolution", ["hjres"] = "house-joint-resolution",
        ["sjres"] = "senate-joint-resolution", ["hconres"] = "house-concurrent-resolution",
        ["sconres"] = "senate-concurrent-resolution",
    };

    public static string FormatRef(string billType, int billNumber) =>
        $"{(RefPrefix.TryGetValue(billType.ToLowerInvariant(), out var p) ? p : billType.ToUpperInvariant())} {billNumber}";

    public static string CongressOrdinal(int congress)
    {
        var n = congress % 100;
        var suffix = n is >= 11 and <= 13 ? "th" : (congress % 10) switch
        {
            1 => "st", 2 => "nd", 3 => "rd", _ => "th",
        };
        return $"{congress}{suffix}";
    }

    public static string CongressGovUrl(int congress, string billType, int billNumber)
    {
        var seg = UrlSegment.TryGetValue(billType.ToLowerInvariant(), out var s) ? s : billType.ToLowerInvariant();
        return $"https://www.congress.gov/bill/{CongressOrdinal(congress)}-congress/{seg}/{billNumber}";
    }

    /// <summary>Collapse HTML to plain text (CRS summary bodies arrive as HTML).</summary>
    public static string StripHtml(string? text) =>
        Regex.Replace(Regex.Replace(text ?? "", "<[^>]+>", " "), @"\s+", " ").Trim();
}
