namespace HousingPolicy.Api.Options;

/// <summary>
/// Configuration for the studies / policy-proposals document store, bound from
/// the "Studies" section. PDFs are kept on local disk for now (a storage
/// bucket comes later); hard size limits guard every upload.
/// </summary>
public sealed class StudiesOptions
{
    public const string SectionName = "Studies";

    /// <summary>Directory for uploaded PDFs. Relative paths resolve under the content root.</summary>
    public string DocumentsDir { get; set; } = "Documents";

    /// <summary>Maximum accepted PDF upload, bytes (hard limit).</summary>
    public long MaxPdfBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>Maximum accepted document-text upload, bytes (hard limit).</summary>
    public long MaxTextBytes { get; set; } = 5 * 1024 * 1024;
}
