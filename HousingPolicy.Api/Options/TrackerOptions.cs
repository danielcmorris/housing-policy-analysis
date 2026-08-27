namespace HousingPolicy.Api.Options;

/// <summary>
/// Hard per-run caps for the legislation-tracker sync, bound from the
/// "Tracker" section. congress.gov allows 5,000 requests/hour; these keep any
/// single refresh/discovery run far below it (same knobs as the Python
/// sync_max_* settings).
/// </summary>
public sealed class TrackerOptions
{
    public const string SectionName = "Tracker";

    /// <summary>List pages per discovery run (x 250 rows each).</summary>
    public int SyncMaxListPages { get; set; } = 8;

    /// <summary>Bill-detail fetches per run.</summary>
    public int SyncMaxDetailCalls { get; set; } = 300;
}
