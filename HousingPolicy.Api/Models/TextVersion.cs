namespace HousingPolicy.Api.Models;

/// <summary>
/// One text version of a bill in one format (e.g. the Enrolled Bill as
/// "Formatted Text"). Column names map from snake_case via Dapper's
/// MatchNamesWithUnderscores. The full body lives in <see cref="TextContent"/>
/// for Formatted-Text rows.
/// </summary>
public sealed class TextVersion
{
    public string VersionCode { get; set; } = "";
    public string? VersionName { get; set; }
    public DateTime? VersionDate { get; set; }
    public string FormatType { get; set; } = "";
    public string? Url { get; set; }
    public string? TextContent { get; set; }
}
