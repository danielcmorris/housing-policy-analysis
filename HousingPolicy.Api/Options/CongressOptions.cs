namespace HousingPolicy.Api.Options;

/// <summary>
/// Configuration for the api.congress.gov client, bound from the "Congress"
/// section (appsettings + environment + creds/config.json). Only the ApiKey is
/// a secret; everything else is a public endpoint/behaviour knob. Mirrors the
/// "config is a registry, not logic" split from the Python config.py.
/// </summary>
public sealed class CongressOptions
{
    public const string SectionName = "Congress";

    /// <summary>Base URL for api.congress.gov v3 (no trailing slash needed).</summary>
    public string BaseUrl { get; set; } = "https://api.congress.gov/v3";

    /// <summary>The api.congress.gov key. Passed as the api_key query param; never logged or stored.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Directory for the raw-zone disk cache of upstream JSON. Relative paths resolve under the content root.</summary>
    public string DataDir { get; set; } = "Data";

    /// <summary>Per-request HTTP timeout, seconds.</summary>
    public double HttpTimeoutSeconds { get; set; } = 60.0;

    /// <summary>Retry attempts for timeouts / 429 / 5xx (in addition to the first try).</summary>
    public int HttpRetries { get; set; } = 3;
}
