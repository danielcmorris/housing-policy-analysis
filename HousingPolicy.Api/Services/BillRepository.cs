using System.Globalization;
using System.Text.Json;
using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Models;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Normalizes congress.gov JSON into schema.sql and reads it back. Parsing
/// helpers are static/pure (unit-testable without a DB); the async methods do
/// the SQL. Everything for one bill is written in a single transaction, and
/// each sub-resource is replaced wholesale on refresh so stale rows never linger.
/// </summary>
public sealed class BillRepository
{
    private readonly DataLayerBase _dl;

    public BillRepository(DataLayerBase dl) => _dl = dl;

    /// <summary>Bill sub-resource endpoints fetched per import (path segments).</summary>
    public static readonly string[] SubResources =
        { "cosponsors", "amendments", "actions", "committees", "summaries", "subjects", "titles", "relatedbills" };

    public static string BillSlug(int congress, string billType, int billNumber) =>
        $"{congress}-{billType.ToLowerInvariant()}-{billNumber}";

    // --- parse result records ------------------------------------------------

    public sealed record ParsedBill(
        int? Congress, string BillType, int? BillNumber, string? Title, string? OriginChamber,
        DateOnly? IntroducedDate, string? PolicyArea,
        DateOnly? LatestActionDate, string? LatestActionText, DateTime? UpdateDate);

    public sealed record ParsedTextVersion(
        string VersionCode, string? VersionName, DateTime? VersionDate, string FormatType, string? Url);

    /// <summary>Everything fetched for one bill, ready to persist in one transaction.</summary>
    public sealed record BillImport(
        int Congress, string BillType, int BillNumber,
        string BillJson, string TextJson,
        ParsedTextVersion? Latest, string? Body,
        IReadOnlyDictionary<string, List<string>> SubResourcePages);

    // --- pure parsers --------------------------------------------------------

    /// <summary>Extract normalized bill columns from a /bill response body.</summary>
    public static ParsedBill ParseBill(string billJson)
    {
        using var doc = JsonDocument.Parse(billJson);
        var b = BillElement(doc.RootElement);

        DateOnly? actionDate = null;
        string? actionText = null;
        if (b.TryGetProperty("latestAction", out var la) && la.ValueKind == JsonValueKind.Object)
        {
            actionDate = GetDate(la, "actionDate");
            actionText = GetString(la, "text");
        }

        string? policyArea = null;
        if (b.TryGetProperty("policyArea", out var pa) && pa.ValueKind == JsonValueKind.Object)
            policyArea = GetString(pa, "name");

        return new ParsedBill(
            Congress: GetInt(b, "congress"),
            BillType: (GetString(b, "type") ?? "").ToLowerInvariant(),
            BillNumber: GetInt(b, "number"),
            Title: GetString(b, "title"),
            OriginChamber: GetString(b, "originChamber"),
            IntroducedDate: GetDate(b, "introducedDate"),
            PolicyArea: policyArea,
            LatestActionDate: actionDate,
            LatestActionText: actionText,
            UpdateDate: GetTimestamp(b, "updateDate") ?? GetTimestamp(b, "updateDateIncludingText"));
    }

    /// <summary>Bill sponsor(s), inline in the /bill payload.</summary>
    public static List<Sponsor> ParseSponsors(string billJson)
    {
        using var doc = JsonDocument.Parse(billJson);
        var b = BillElement(doc.RootElement);
        var list = new List<Sponsor>();
        if (!b.TryGetProperty("sponsors", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var s in arr.EnumerateArray())
        {
            var bioguide = GetString(s, "bioguideId");
            if (string.IsNullOrEmpty(bioguide)) continue;
            list.Add(new Sponsor
            {
                BioguideId = bioguide,
                FullName = GetString(s, "fullName"),
                FirstName = GetString(s, "firstName"),
                LastName = GetString(s, "lastName"),
                Party = GetString(s, "party"),
                State = GetString(s, "state"),
                District = GetInt(s, "district"),
                IsByRequest = GetString(s, "isByRequest"),
                Url = GetString(s, "url"),
            });
        }
        return list;
    }

    public static List<Cosponsor> ParseCosponsors(List<string> pages) =>
        ParsePages(pages, "cosponsors", c =>
        {
            var bioguide = GetString(c, "bioguideId");
            return string.IsNullOrEmpty(bioguide) ? null : new Cosponsor
            {
                BioguideId = bioguide,
                FullName = GetString(c, "fullName"),
                Party = GetString(c, "party"),
                State = GetString(c, "state"),
                District = GetInt(c, "district"),
                IsOriginalCosponsor = GetBool(c, "isOriginalCosponsor"),
                SponsorshipDate = GetDate(c, "sponsorshipDate"),
                Url = GetString(c, "url"),
            };
        });

    public static List<Amendment> ParseAmendments(List<string> pages) =>
        ParsePages(pages, "amendments", a =>
        {
            var type = GetString(a, "type");
            var number = GetString(a, "number");
            return (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(number)) ? null : new Amendment
            {
                AmendmentCongress = GetInt(a, "congress"),
                AmendmentType = type,
                AmendmentNumber = number,
                UpdateDate = GetTimestamp(a, "updateDate"),
                Url = GetString(a, "url"),
            };
        });

    public static List<BillAction> ParseActions(List<string> pages)
    {
        var list = new List<BillAction>();
        var ordinal = 0;
        foreach (var page in pages)
        {
            using var doc = JsonDocument.Parse(page);
            if (!doc.RootElement.TryGetProperty("actions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var a in arr.EnumerateArray())
            {
                int? code = null; string? name = null;
                if (a.TryGetProperty("sourceSystem", out var ss) && ss.ValueKind == JsonValueKind.Object)
                {
                    code = GetInt(ss, "code");
                    name = GetString(ss, "name");
                }
                list.Add(new BillAction
                {
                    Ordinal = ordinal++,
                    ActionDate = GetDate(a, "actionDate"),
                    ActionCode = GetString(a, "actionCode"),
                    ActionType = GetString(a, "type"),
                    SourceSystemCode = code,
                    SourceSystemName = name,
                    Text = GetString(a, "text"),
                });
            }
        }
        return list;
    }

    public static List<Committee> ParseCommittees(List<string> pages) =>
        ParsePages(pages, "committees", c =>
        {
            var systemCode = GetString(c, "systemCode");
            return string.IsNullOrEmpty(systemCode) ? null : new Committee
            {
                SystemCode = systemCode,
                Chamber = GetString(c, "chamber"),
                Name = GetString(c, "name"),
                Type = GetString(c, "type"),
                Url = GetString(c, "url"),
                Activities = GetRawJson(c, "activities"),
            };
        });

    public static List<Subject> ParseSubjects(List<string> pages)
    {
        var list = new List<Subject>();
        var seen = new HashSet<string>();
        foreach (var page in pages)
        {
            using var doc = JsonDocument.Parse(page);
            if (!doc.RootElement.TryGetProperty("subjects", out var subj) || subj.ValueKind != JsonValueKind.Object)
                continue;
            if (!subj.TryGetProperty("legislativeSubjects", out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var s in arr.EnumerateArray())
            {
                var name = GetString(s, "name");
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                list.Add(new Subject { Name = name, UpdateDate = GetTimestamp(s, "updateDate") });
            }
        }
        return list;
    }

    public static List<Summary> ParseSummaries(List<string> pages) =>
        ParsePages(pages, "summaries", s =>
        {
            var version = GetString(s, "versionCode");
            return string.IsNullOrEmpty(version) ? null : new Summary
            {
                VersionCode = version,
                ActionDate = GetDate(s, "actionDate"),
                ActionDesc = GetString(s, "actionDesc"),
                UpdateDate = GetTimestamp(s, "updateDate"),
                Text = GetString(s, "text"),
            };
        });

    public static List<BillTitle> ParseTitles(List<string> pages)
    {
        var list = new List<BillTitle>();
        var ordinal = 0;
        foreach (var page in pages)
        {
            using var doc = JsonDocument.Parse(page);
            if (!doc.RootElement.TryGetProperty("titles", out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var t in arr.EnumerateArray())
                list.Add(new BillTitle
                {
                    Ordinal = ordinal++,
                    Title = GetString(t, "title"),
                    TitleType = GetString(t, "titleType"),
                    TitleTypeCode = GetInt(t, "titleTypeCode"),
                    BillTextVersionCode = GetString(t, "billTextVersionCode"),
                    ChamberCode = GetString(t, "chamberCode"),
                    ChamberName = GetString(t, "chamberName"),
                    UpdateDate = GetTimestamp(t, "updateDate"),
                });
        }
        return list;
    }

    public static List<RelatedBill> ParseRelatedBills(List<string> pages) =>
        ParsePages(pages, "relatedBills", r =>
        {
            var type = GetString(r, "type");
            var number = GetInt(r, "number");
            var congress = GetInt(r, "congress");
            if (string.IsNullOrEmpty(type) || number is null || congress is null) return null;
            DateOnly? laDate = null; string? laText = null;
            if (r.TryGetProperty("latestAction", out var la) && la.ValueKind == JsonValueKind.Object)
            {
                laDate = GetDate(la, "actionDate");
                laText = GetString(la, "text");
            }
            return new RelatedBill
            {
                RelatedCongress = congress.Value,
                RelatedType = type,
                RelatedNumber = number.Value,
                Title = GetString(r, "title"),
                LatestActionDate = laDate,
                LatestActionText = laText,
                RelationshipDetails = GetRawJson(r, "relationshipDetails"),
                Url = GetString(r, "url"),
            };
        });

    /// <summary>
    /// Pick the single most recent raw text: congress.gov lists textVersions
    /// newest-first (latest legislative stage — Enrolled if the bill passed),
    /// so we take the first version that offers a "Formatted Text" format.
    /// Returns null if the bill has no formatted-text version.
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

    // --- SQL: write ----------------------------------------------------------

    /// <summary>Upsert a bill, its latest text, all metadata sub-resources, and raw payloads in one transaction. Returns the bill_id slug.</summary>
    public async Task<string> StoreLawAsync(BillImport import, CancellationToken ct = default)
    {
        var slug = BillSlug(import.Congress, import.BillType, import.BillNumber);
        var b = ParseBill(import.BillJson);
        var vintage = DateTime.UtcNow;

        List<string> Pages(string res) => import.SubResourcePages.TryGetValue(res, out var p) ? p : new();

        var sponsors = ParseSponsors(import.BillJson);
        var cosponsors = ParseCosponsors(Pages("cosponsors"));
        var amendments = ParseAmendments(Pages("amendments"));
        var actions = ParseActions(Pages("actions"));
        var committees = ParseCommittees(Pages("committees"));
        var summaries = ParseSummaries(Pages("summaries"));
        var subjects = ParseSubjects(Pages("subjects"));
        var titles = ParseTitles(Pages("titles"));
        var related = ParseRelatedBills(Pages("relatedbills"));

        await using var con = await _dl.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        async Task Exec(string sql, object? p) =>
            await con.ExecuteAsync(new CommandDefinition(sql, p, transaction: tx, cancellationToken: ct));

        await Exec(
            """
            INSERT INTO bills (bill_id, congress, bill_type, bill_number, title, origin_chamber,
                               introduced_date, policy_area, latest_action_date, latest_action_text,
                               update_date, source_id, data_vintage)
            VALUES (@BillId, @Congress, @BillType, @BillNumber, @Title, @OriginChamber,
                    @IntroducedDate, @PolicyArea, @LatestActionDate, @LatestActionText,
                    @UpdateDate, 'congress_gov', @DataVintage)
            ON CONFLICT (bill_id) DO UPDATE SET
                title = EXCLUDED.title, origin_chamber = EXCLUDED.origin_chamber,
                introduced_date = EXCLUDED.introduced_date, policy_area = EXCLUDED.policy_area,
                latest_action_date = EXCLUDED.latest_action_date, latest_action_text = EXCLUDED.latest_action_text,
                update_date = EXCLUDED.update_date, data_vintage = EXCLUDED.data_vintage
            """,
            new
            {
                BillId = slug, Congress = import.Congress, BillType = import.BillType.ToLowerInvariant(),
                BillNumber = import.BillNumber, b.Title, b.OriginChamber, b.IntroducedDate, b.PolicyArea,
                b.LatestActionDate, b.LatestActionText, b.UpdateDate, DataVintage = vintage,
            });

        // Replace all per-bill child rows, then re-insert.
        foreach (var table in new[]
                 {
                     "bill_text_versions", "bill_sponsors", "bill_cosponsors", "bill_amendments",
                     "bill_actions", "bill_committees", "bill_subjects", "bill_summaries",
                     "bill_titles", "bill_related_bills",
                 })
            await Exec($"DELETE FROM {table} WHERE bill_id = @BillId", new { BillId = slug });

        if (import.Latest is { } lv)
            await Exec(
                """
                INSERT INTO bill_text_versions
                    (bill_id, version_code, version_name, version_date, format_type, url, text_content)
                VALUES (@BillId, @VersionCode, @VersionName, @VersionDate, @FormatType, @Url, @TextContent)
                """,
                new { BillId = slug, lv.VersionCode, lv.VersionName, lv.VersionDate, lv.FormatType, lv.Url, TextContent = import.Body });

        if (sponsors.Count > 0)
            await Exec(
                """
                INSERT INTO bill_sponsors (bill_id, bioguide_id, full_name, first_name, last_name, party, state, district, is_by_request, url)
                VALUES (@BillId, @BioguideId, @FullName, @FirstName, @LastName, @Party, @State, @District, @IsByRequest, @Url)
                """,
                sponsors.Select(s => new { BillId = slug, s.BioguideId, s.FullName, s.FirstName, s.LastName, s.Party, s.State, s.District, s.IsByRequest, s.Url }));

        if (cosponsors.Count > 0)
            await Exec(
                """
                INSERT INTO bill_cosponsors (bill_id, bioguide_id, full_name, party, state, district, is_original_cosponsor, sponsorship_date, url)
                VALUES (@BillId, @BioguideId, @FullName, @Party, @State, @District, @IsOriginalCosponsor, @SponsorshipDate, @Url)
                ON CONFLICT (bill_id, bioguide_id) DO NOTHING
                """,
                cosponsors.Select(c => new { BillId = slug, c.BioguideId, c.FullName, c.Party, c.State, c.District, c.IsOriginalCosponsor, c.SponsorshipDate, c.Url }));

        if (amendments.Count > 0)
            await Exec(
                """
                INSERT INTO bill_amendments (bill_id, amendment_congress, amendment_type, amendment_number, update_date, url)
                VALUES (@BillId, @AmendmentCongress, @AmendmentType, @AmendmentNumber, @UpdateDate, @Url)
                ON CONFLICT (bill_id, amendment_type, amendment_number) DO NOTHING
                """,
                amendments.Select(a => new { BillId = slug, a.AmendmentCongress, a.AmendmentType, a.AmendmentNumber, a.UpdateDate, a.Url }));

        if (actions.Count > 0)
            await Exec(
                """
                INSERT INTO bill_actions (bill_id, ordinal, action_date, action_code, action_type, source_system_code, source_system_name, text)
                VALUES (@BillId, @Ordinal, @ActionDate, @ActionCode, @ActionType, @SourceSystemCode, @SourceSystemName, @Text)
                """,
                actions.Select(a => new { BillId = slug, a.Ordinal, a.ActionDate, a.ActionCode, a.ActionType, a.SourceSystemCode, a.SourceSystemName, a.Text }));

        if (committees.Count > 0)
            await Exec(
                """
                INSERT INTO bill_committees (bill_id, system_code, chamber, name, type, url, activities)
                VALUES (@BillId, @SystemCode, @Chamber, @Name, @Type, @Url, CAST(@Activities AS jsonb))
                ON CONFLICT (bill_id, system_code) DO NOTHING
                """,
                committees.Select(c => new { BillId = slug, c.SystemCode, c.Chamber, c.Name, c.Type, c.Url, c.Activities }));

        if (subjects.Count > 0)
            await Exec(
                """
                INSERT INTO bill_subjects (bill_id, name, update_date)
                VALUES (@BillId, @Name, @UpdateDate)
                ON CONFLICT (bill_id, name) DO NOTHING
                """,
                subjects.Select(s => new { BillId = slug, s.Name, s.UpdateDate }));

        if (summaries.Count > 0)
            await Exec(
                """
                INSERT INTO bill_summaries (bill_id, version_code, action_date, action_desc, update_date, text)
                VALUES (@BillId, @VersionCode, @ActionDate, @ActionDesc, @UpdateDate, @Text)
                ON CONFLICT (bill_id, version_code) DO NOTHING
                """,
                summaries.Select(s => new { BillId = slug, s.VersionCode, s.ActionDate, s.ActionDesc, s.UpdateDate, s.Text }));

        if (titles.Count > 0)
            await Exec(
                """
                INSERT INTO bill_titles (bill_id, ordinal, title, title_type, title_type_code, bill_text_version_code, chamber_code, chamber_name, update_date)
                VALUES (@BillId, @Ordinal, @Title, @TitleType, @TitleTypeCode, @BillTextVersionCode, @ChamberCode, @ChamberName, @UpdateDate)
                """,
                titles.Select(t => new { BillId = slug, t.Ordinal, t.Title, t.TitleType, t.TitleTypeCode, t.BillTextVersionCode, t.ChamberCode, t.ChamberName, t.UpdateDate }));

        if (related.Count > 0)
            await Exec(
                """
                INSERT INTO bill_related_bills (bill_id, related_congress, related_type, related_number, title, latest_action_date, latest_action_text, relationship_details, url)
                VALUES (@BillId, @RelatedCongress, @RelatedType, @RelatedNumber, @Title, @LatestActionDate, @LatestActionText, CAST(@RelationshipDetails AS jsonb), @Url)
                ON CONFLICT (bill_id, related_congress, related_type, related_number) DO NOTHING
                """,
                related.Select(r => new { BillId = slug, r.RelatedCongress, r.RelatedType, r.RelatedNumber, r.Title, r.LatestActionDate, r.LatestActionText, r.RelationshipDetails, r.Url }));

        foreach (var (endpoint, payload) in new[] { ("bill", import.BillJson), ("text", import.TextJson) })
            await Exec(
                """
                INSERT INTO raw_payloads (bill_id, endpoint, fetched_at, http_status, payload_json)
                VALUES (@BillId, @Endpoint, @FetchedAt, @HttpStatus, CAST(@Payload AS jsonb))
                """,
                new { BillId = slug, Endpoint = endpoint, FetchedAt = vintage, HttpStatus = 200, Payload = payload });

        await tx.CommitAsync(ct);
        return slug;
    }

    // --- SQL: read -----------------------------------------------------------

    /// <summary>Assemble a stored bill + its text and all metadata, or null if absent.</summary>
    public async Task<Bill?> GetBillAsync(string billId)
    {
        var bill = await _dl.QuerySingleOrDefaultAsync<Bill>(
            "SELECT * FROM bills WHERE bill_id = @BillId", new { BillId = billId });
        if (bill is null) return null;

        var p = new { BillId = billId };
        bill.TextVersions = (await _dl.QueryAsync<TextVersion>(
            "SELECT version_code, version_name, version_date, format_type, url, text_content FROM bill_text_versions WHERE bill_id = @BillId ORDER BY version_date, format_type", p)).ToList();
        bill.Sponsors = (await _dl.QueryAsync<Sponsor>(
            "SELECT bioguide_id, full_name, first_name, last_name, party, state, district, is_by_request, url FROM bill_sponsors WHERE bill_id = @BillId", p)).ToList();
        bill.Cosponsors = (await _dl.QueryAsync<Cosponsor>(
            "SELECT bioguide_id, full_name, party, state, district, is_original_cosponsor, sponsorship_date, url FROM bill_cosponsors WHERE bill_id = @BillId ORDER BY sponsorship_date", p)).ToList();
        bill.Amendments = (await _dl.QueryAsync<Amendment>(
            "SELECT amendment_congress, amendment_type, amendment_number, update_date, url FROM bill_amendments WHERE bill_id = @BillId ORDER BY update_date DESC", p)).ToList();
        bill.Actions = (await _dl.QueryAsync<BillAction>(
            "SELECT ordinal, action_date, action_code, action_type, source_system_code, source_system_name, text FROM bill_actions WHERE bill_id = @BillId ORDER BY ordinal", p)).ToList();
        bill.Committees = (await _dl.QueryAsync<Committee>(
            "SELECT system_code, chamber, name, type, url, activities::text AS activities FROM bill_committees WHERE bill_id = @BillId", p)).ToList();
        bill.Subjects = (await _dl.QueryAsync<Subject>(
            "SELECT name, update_date FROM bill_subjects WHERE bill_id = @BillId ORDER BY name", p)).ToList();
        bill.Summaries = (await _dl.QueryAsync<Summary>(
            "SELECT version_code, action_date, action_desc, update_date, text FROM bill_summaries WHERE bill_id = @BillId ORDER BY action_date", p)).ToList();
        bill.Titles = (await _dl.QueryAsync<BillTitle>(
            "SELECT ordinal, title, title_type, title_type_code, bill_text_version_code, chamber_code, chamber_name, update_date FROM bill_titles WHERE bill_id = @BillId ORDER BY ordinal", p)).ToList();
        bill.RelatedBills = (await _dl.QueryAsync<RelatedBill>(
            "SELECT related_congress, related_type, related_number, title, latest_action_date, latest_action_text, relationship_details::text AS relationship_details, url FROM bill_related_bills WHERE bill_id = @BillId", p)).ToList();
        return bill;
    }

    // --- JSON helpers --------------------------------------------------------

    private static JsonElement BillElement(JsonElement root) =>
        root.TryGetProperty("bill", out var b) ? b : root;

    /// <summary>Map every item of a top-level array collection across pages; map returning null drops the item.</summary>
    private static List<T> ParsePages<T>(List<string> pages, string collectionKey, Func<JsonElement, T?> map)
        where T : class
    {
        var list = new List<T>();
        foreach (var page in pages)
        {
            using var doc = JsonDocument.Parse(page);
            if (!doc.RootElement.TryGetProperty(collectionKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var item in arr.EnumerateArray())
                if (map(item) is { } mapped)
                    list.Add(mapped);
        }
        return list;
    }

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

    private static bool? GetBool(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    /// <summary>Raw JSON text of an array/object property (for JSONB columns), or null.</summary>
    private static string? GetRawJson(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? v.GetRawText() : null;

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
