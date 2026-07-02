using System.Globalization;
using System.Text.Json;
using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Models;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Normalizes congress.gov JSON into schema.sql and reads it back. Parsing
/// helpers are static/pure (unit-testable without a DB); the async methods do
/// the SQL. Upserts use ON CONFLICT so re-pulling a bill (?refresh=true) is
/// idempotent — the Python repository.py in C#/Dapper form.
/// </summary>
public sealed class BillRepository
{
    private readonly DataLayerBase _dl;

    public BillRepository(DataLayerBase dl) => _dl = dl;

    public static string BillSlug(int congress, string billType, int billNumber) =>
        $"{congress}-{billType.ToLowerInvariant()}-{billNumber}";

    // --- pure parsers --------------------------------------------------------

    public sealed record ParsedBill(
        int? Congress, string BillType, int? BillNumber, string? Title, string? OriginChamber,
        DateOnly? LatestActionDate, string? LatestActionText, DateTime? UpdateDate);

    public sealed record ParsedTextVersion(
        string VersionCode, string? VersionName, DateTime? VersionDate, string FormatType, string? Url);

    /// <summary>Extract normalized bill columns from a /bill response body.</summary>
    public static ParsedBill ParseBill(string billJson)
    {
        using var doc = JsonDocument.Parse(billJson);
        var root = doc.RootElement;
        var b = root.TryGetProperty("bill", out var billEl) ? billEl : root;

        DateOnly? actionDate = null;
        string? actionText = null;
        if (b.TryGetProperty("latestAction", out var la) && la.ValueKind == JsonValueKind.Object)
        {
            actionDate = GetDate(la, "actionDate");
            actionText = GetString(la, "text");
        }

        return new ParsedBill(
            Congress: GetInt(b, "congress"),
            BillType: (GetString(b, "type") ?? "").ToLowerInvariant(),
            BillNumber: GetInt(b, "number"),
            Title: GetString(b, "title"),
            OriginChamber: GetString(b, "originChamber"),
            LatestActionDate: actionDate,
            LatestActionText: actionText,
            UpdateDate: GetTimestamp(b, "updateDate") ?? GetTimestamp(b, "updateDateIncludingText"));
    }

    /// <summary>
    /// Pick the single most recent raw text: congress.gov lists textVersions
    /// newest-first (latest legislative stage — Enrolled if the bill passed),
    /// so we take the first version that offers a "Formatted Text" format.
    /// Returns null if the bill has no formatted-text version. (We keep only
    /// the latest version's text, not every stage — see StoreLawAsync.)
    /// </summary>
    public static ParsedTextVersion? SelectLatestFormattedText(string textJson)
    {
        using var doc = JsonDocument.Parse(textJson);
        if (!doc.RootElement.TryGetProperty("textVersions", out var versions) ||
            versions.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var v in versions.EnumerateArray())
        {
            if (!v.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var fmt in formats.EnumerateArray())
            {
                if (GetString(fmt, "type") != "Formatted Text")
                    continue;
                var name = GetString(v, "type");
                var url = GetString(fmt, "url");
                return new ParsedTextVersion(
                    VersionCode: CongressClient.VersionCode(name, url),
                    VersionName: name,
                    VersionDate: GetTimestamp(v, "date"),
                    FormatType: "Formatted Text",
                    Url: url);
            }
        }
        return null;
    }

    // --- SQL -----------------------------------------------------------------

    /// <summary>
    /// Upsert a bill + its single most-recent text version (+ raw payloads) in
    /// one transaction. <paramref name="latest"/>/<paramref name="body"/> come
    /// from <see cref="SelectLatestFormattedText"/>; pass null to store metadata
    /// only. Returns the bill_id slug.
    /// </summary>
    public async Task<string> StoreLawAsync(
        int congress, string billType, int billNumber,
        string billJson, string textJson, ParsedTextVersion? latest, string? body,
        CancellationToken ct = default)
    {
        var slug = BillSlug(congress, billType, billNumber);
        var b = ParseBill(billJson);
        var vintage = DateTime.UtcNow;

        await using var con = await _dl.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        await con.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bills (bill_id, congress, bill_type, bill_number, title,
                               origin_chamber, latest_action_date, latest_action_text,
                               update_date, source_id, data_vintage)
            VALUES (@BillId, @Congress, @BillType, @BillNumber, @Title,
                    @OriginChamber, @LatestActionDate, @LatestActionText,
                    @UpdateDate, 'congress_gov', @DataVintage)
            ON CONFLICT (bill_id) DO UPDATE SET
                title = EXCLUDED.title,
                origin_chamber = EXCLUDED.origin_chamber,
                latest_action_date = EXCLUDED.latest_action_date,
                latest_action_text = EXCLUDED.latest_action_text,
                update_date = EXCLUDED.update_date,
                data_vintage = EXCLUDED.data_vintage
            """,
            new
            {
                BillId = slug,
                Congress = congress,
                BillType = billType.ToLowerInvariant(),
                BillNumber = billNumber,
                b.Title,
                b.OriginChamber,
                b.LatestActionDate,
                b.LatestActionText,
                b.UpdateDate,
                DataVintage = vintage,
            },
            transaction: tx, cancellationToken: ct));

        // Keep only the most recent version's raw text: clear any prior rows
        // (e.g. a bill first stored under the old multi-version scheme) and
        // insert just the latest.
        await con.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bill_text_versions WHERE bill_id = @BillId",
            new { BillId = slug }, transaction: tx, cancellationToken: ct));

        if (latest is not null)
        {
            await con.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bill_text_versions
                    (bill_id, version_code, version_name, version_date, format_type, url, text_content)
                VALUES (@BillId, @VersionCode, @VersionName, @VersionDate, @FormatType, @Url, @TextContent)
                ON CONFLICT (bill_id, version_code, format_type) DO UPDATE SET
                    version_name = EXCLUDED.version_name,
                    version_date = EXCLUDED.version_date,
                    url = EXCLUDED.url,
                    text_content = EXCLUDED.text_content
                """,
                new
                {
                    BillId = slug,
                    latest.VersionCode,
                    latest.VersionName,
                    latest.VersionDate,
                    latest.FormatType,
                    latest.Url,
                    TextContent = body,
                },
                transaction: tx, cancellationToken: ct));
        }

        foreach (var (endpoint, payload) in new[] { ("bill", billJson), ("text", textJson) })
        {
            await con.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO raw_payloads (bill_id, endpoint, fetched_at, http_status, payload_json)
                VALUES (@BillId, @Endpoint, @FetchedAt, @HttpStatus, CAST(@Payload AS jsonb))
                """,
                new { BillId = slug, Endpoint = endpoint, FetchedAt = vintage, HttpStatus = 200, Payload = payload },
                transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return slug;
    }

    /// <summary>Assemble a stored bill + its text versions, or null if absent.</summary>
    public async Task<Bill?> GetBillAsync(string billId)
    {
        var bill = await _dl.QuerySingleOrDefaultAsync<Bill>(
            "SELECT * FROM bills WHERE bill_id = @BillId", new { BillId = billId });
        if (bill is null) return null;

        var versions = await _dl.QueryAsync<TextVersion>(
            """
            SELECT version_code, version_name, version_date, format_type, url, text_content
            FROM bill_text_versions WHERE bill_id = @BillId
            ORDER BY version_date, format_type
            """,
            new { BillId = billId });
        bill.TextVersions = versions.ToList();
        return bill;
    }

    // --- JSON helpers --------------------------------------------------------

    private static string? GetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static DateOnly? GetDate(JsonElement e, string prop)
    {
        var s = GetString(e, prop);
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static DateTime? GetTimestamp(JsonElement e, string prop)
    {
        var s = GetString(e, prop);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt.UtcDateTime   // Kind=Utc, required by Npgsql for timestamptz
            : null;
    }
}
