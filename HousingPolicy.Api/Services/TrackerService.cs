using System.Text.Json;
using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// The legislation tracker (port of the Python legislation.py): discovers and
/// maintains housing bills in Postgres, curates tracked/untracked and
/// display/pin state, tags CRS summaries, and serves the tracker feed.
///
/// congress.gov's list endpoint cannot filter by policy area, so discovery
/// walks recently-updated bills and keeps those whose detail carries the
/// housing policy area. Every congress.gov-touching run is bounded by the
/// TrackerOptions caps.
/// </summary>
public sealed class TrackerService
{
    private readonly DataLayerBase _dl;
    private readonly CongressClient _congress;
    private readonly TrackerOptions _opt;
    private readonly ILogger<TrackerService> _log;

    public TrackerService(DataLayerBase dl, CongressClient congress,
                          IOptions<TrackerOptions> options, ILogger<TrackerService> log)
    {
        _dl = dl;
        _congress = congress;
        _opt = options.Value;
        _log = log;
    }

    // --- DTOs (serialized snake_case to match the Angular client) ------------

    public sealed record TrackerListRow(
        string BillId, string TrackingStatus, bool HasText, string[] Tags, string? TagsSource,
        bool Watch, string? DisplayDate, bool Displayed, bool Pinned,
        string Ref, string Congress, string? Chamber, string? Title,
        string StatusKey, string? StatusText, string? Updated, string? Introduced,
        string? Category, string? Sponsor, string? SponsorParty, string? SponsorState,
        string? Summary, string CongressGovUrl);

    public sealed record Candidate(
        int Congress, string BillType, int BillNumber, string Ref, string? Title,
        string? Chamber, string? Sponsor, string? Introduced,
        string? LatestActionDate, string? LatestActionText, string StatusKey,
        string[] Tags, bool Watch);

    public sealed record DiscoverResult(string Mode, int Days, int Listed, int DetailCalls,
                                        List<Candidate> Candidates);

    public sealed record RefreshResult(string Mode, int Bills, int Refreshed,
                                       List<string> TextsPulled, int Calls);

    // --- ingest --------------------------------------------------------------

    /// <summary>
    /// Detail-fetch one bill (+ its CRS summaries); store it. Housing bills
    /// only unless requireHousing is false. Curated columns (tracking_status,
    /// display_date, pinned, and non-provisional tags) are never overwritten.
    /// Returns the slug, or null when filtered out.
    /// </summary>
    public async Task<string?> IngestDetailAsync(
        int congress, string billType, int billNumber,
        string tracking = "tracked", bool requireHousing = true, bool refresh = true,
        CancellationToken ct = default)
    {
        var billJson = await _congress.FetchBillAsync(congress, billType, billNumber, refresh, ct);
        var b = BillRepository.ParseBill(billJson);
        if (requireHousing && b.PolicyArea != TrackerRules.HousingPolicyArea)
            return null;

        List<string> summaryPages;
        try
        {
            summaryPages = await _congress.FetchSubResourcePagesAsync(
                congress, billType, billNumber, "summaries", refresh: true, ct);
        }
        catch (BillNotFoundException)
        {
            summaryPages = new List<string>();
        }

        var slug = BillRepository.BillSlug(congress, billType, billNumber);
        var sponsors = BillRepository.ParseSponsors(billJson);
        var summaries = BillRepository.ParseSummaries(summaryPages)
            .Select(s => new { s.VersionCode, s.ActionDate, s.ActionDesc, s.UpdateDate,
                               Text = TrackerRules.StripHtml(s.Text) })
            .ToList();
        var vintage = DateTime.UtcNow;

        await using var con = await _dl.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        async Task Exec(string sql, object? p) =>
            await con.ExecuteAsync(new CommandDefinition(sql, p, transaction: tx, cancellationToken: ct));

        await Exec(
            """
            INSERT INTO bills (bill_id, congress, bill_type, bill_number, title, origin_chamber,
                               introduced_date, policy_area, latest_action_date, latest_action_text,
                               update_date, source_id, data_vintage, tracking_status)
            VALUES (@BillId, @Congress, @BillType, @BillNumber, @Title, @OriginChamber,
                    @IntroducedDate, @PolicyArea, @LatestActionDate, @LatestActionText,
                    @UpdateDate, 'congress_gov', @DataVintage, @Tracking)
            ON CONFLICT (bill_id) DO UPDATE SET
                title = EXCLUDED.title, origin_chamber = EXCLUDED.origin_chamber,
                introduced_date = EXCLUDED.introduced_date, policy_area = EXCLUDED.policy_area,
                latest_action_date = EXCLUDED.latest_action_date,
                latest_action_text = EXCLUDED.latest_action_text,
                update_date = EXCLUDED.update_date, data_vintage = EXCLUDED.data_vintage
            """,
            new
            {
                BillId = slug, Congress = congress, BillType = billType.ToLowerInvariant(),
                BillNumber = billNumber, b.Title, b.OriginChamber, b.IntroducedDate, b.PolicyArea,
                b.LatestActionDate, b.LatestActionText, b.UpdateDate, DataVintage = vintage,
                Tracking = tracking,
            });

        await Exec("DELETE FROM bill_sponsors WHERE bill_id = @BillId", new { BillId = slug });
        if (sponsors.Count > 0)
            await Exec(
                """
                INSERT INTO bill_sponsors (bill_id, bioguide_id, full_name, first_name, last_name,
                                           party, state, district, is_by_request, url)
                VALUES (@BillId, @BioguideId, @FullName, @FirstName, @LastName,
                        @Party, @State, @District, @IsByRequest, @Url)
                ON CONFLICT (bill_id, bioguide_id) DO NOTHING
                """,
                sponsors.Select(s => new { BillId = slug, s.BioguideId, s.FullName, s.FirstName,
                                           s.LastName, s.Party, s.State, s.District, s.IsByRequest, s.Url }));

        if (summaries.Count > 0)
        {
            await Exec("DELETE FROM bill_summaries WHERE bill_id = @BillId", new { BillId = slug });
            await Exec(
                """
                INSERT INTO bill_summaries (bill_id, version_code, action_date, action_desc, update_date, text)
                VALUES (@BillId, @VersionCode, @ActionDate, @ActionDesc, @UpdateDate, @Text)
                ON CONFLICT (bill_id, version_code) DO NOTHING
                """,
                summaries.Select(s => new { BillId = slug, s.VersionCode, s.ActionDate,
                                            s.ActionDesc, s.UpdateDate, s.Text }));
        }

        // Tagging. The CRS-summary scan is authoritative and runs once — when
        // the summary first arrives (upgrading provisional title-scan tags,
        // never touching 'summary'/'manual' tags). Bills without a summary get
        // provisional title tags so the review list stays usable.
        var (existingTags, tagsSource) = (await con.QuerySingleAsync<(string[], string?)>(
            new CommandDefinition("SELECT tags, tags_source FROM bills WHERE bill_id = @BillId",
                new { BillId = slug }, transaction: tx, cancellationToken: ct)));
        if (summaries.Count > 0 && tagsSource is null or "title")
        {
            var tags = TrackerRules.DeriveTags(b.Title + " " + string.Join(" ", summaries.Select(s => s.Text)));
            await Exec("UPDATE bills SET tags = @Tags, tags_source = 'summary' WHERE bill_id = @BillId",
                       new { Tags = tags, BillId = slug });
        }
        else if (summaries.Count == 0 && existingTags.Length == 0 && tagsSource is null)
        {
            var tags = TrackerRules.DeriveTags(b.Title + " " + b.LatestActionText);
            if (tags.Length > 0)
                await Exec("UPDATE bills SET tags = @Tags, tags_source = 'title' WHERE bill_id = @BillId",
                           new { Tags = tags, BillId = slug });
        }

        await Exec(
            """
            INSERT INTO raw_payloads (bill_id, endpoint, fetched_at, http_status, payload_json)
            VALUES (@BillId, 'bill', @FetchedAt, 200, CAST(@Payload AS jsonb))
            """,
            new { BillId = slug, FetchedAt = vintage, Payload = billJson });

        await tx.CommitAsync(ct);
        return slug;
    }

    /// <summary>Fetch + store the latest Formatted-Text body for one bill; bumps data_vintage.</summary>
    public async Task StoreTextsForAsync(string billId, int congress, string billType, int billNumber,
                                         CancellationToken ct = default)
    {
        string textJson;
        try
        {
            textJson = await _congress.FetchBillTextAsync(congress, billType, billNumber, refresh: true, ct);
        }
        catch (BillNotFoundException)
        {
            return;
        }
        var latest = BillRepository.SelectLatestFormattedText(textJson);
        if (latest is null || string.IsNullOrEmpty(latest.Url))
            return;
        var body = await _congress.FetchTextBodyAsync(latest.Url, ct);

        await using var con = await _dl.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await con.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bill_text_versions WHERE bill_id = @BillId",
            new { BillId = billId }, transaction: tx, cancellationToken: ct));
        await con.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bill_text_versions
                (bill_id, version_code, version_name, version_date, format_type, url, text_content)
            VALUES (@BillId, @VersionCode, @VersionName, @VersionDate, @FormatType, @Url, @Body)
            """,
            new { BillId = billId, latest.VersionCode, latest.VersionName, latest.VersionDate,
                  latest.FormatType, latest.Url, Body = body },
            transaction: tx, cancellationToken: ct));
        await con.ExecuteAsync(new CommandDefinition(
            "UPDATE bills SET data_vintage = @Now WHERE bill_id = @BillId",
            new { Now = DateTime.UtcNow, BillId = billId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    // --- admin operations ----------------------------------------------------

    // Mutable row classes: Dapper materializes these by property name (with
    // MatchNamesWithUnderscores), which is what handles DateOnly/TEXT[] columns.
    private sealed class BillKeyRow
    {
        public string BillId { get; set; } = "";
        public int Congress { get; set; }
        public string BillType { get; set; } = "";
        public int BillNumber { get; set; }
        public string? UpdateDate { get; set; }
        public bool HasText { get; set; }
        public string TrackingStatus { get; set; } = "";
    }

    /// <summary>Re-sync every tracked bill; re-pull text where missing or changed upstream.</summary>
    public async Task<RefreshResult> RefreshTrackedAsync(int congress, CancellationToken ct = default)
    {
        var targets = (await _dl.QueryAsync<BillKeyRow>(
            """
            SELECT b.bill_id, b.congress, b.bill_type, b.bill_number, b.update_date::text AS update_date,
                   EXISTS (SELECT 1 FROM bill_text_versions tv
                           WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL) AS has_text,
                   b.tracking_status
            FROM bills b
            WHERE b.tracking_status = 'tracked' AND b.congress = @Congress
            ORDER BY b.latest_action_date DESC NULLS LAST
            """, new { Congress = congress })).ToList();

        var calls = 0;
        var refreshed = 0;
        var textsPulled = new List<string>();
        foreach (var t in targets)
        {
            if (calls >= _opt.SyncMaxDetailCalls) break;
            calls += 2; // detail + summaries
            try
            {
                await IngestDetailAsync(t.Congress, t.BillType, t.BillNumber,
                                        requireHousing: false, ct: ct);
            }
            catch (BillNotFoundException)
            {
                continue;
            }
            refreshed++;

            var newUpdate = await _dl.QuerySingleOrDefaultAsync<string>(
                "SELECT update_date::text FROM bills WHERE bill_id = @BillId", new { t.BillId });
            var changed = t.UpdateDate is null || newUpdate is null ||
                          !t.UpdateDate.StartsWith(newUpdate[..Math.Min(19, newUpdate.Length)], StringComparison.Ordinal);
            if (!t.HasText || changed)
            {
                calls += 2;
                await StoreTextsForAsync(t.BillId, t.Congress, t.BillType, t.BillNumber, ct);
                textsPulled.Add(t.BillId);
            }
        }

        await SetSyncStateAsync("last_refresh_at", DateTime.UtcNow.ToString("o"));
        return new RefreshResult("refresh", targets.Count, refreshed, textsPulled, calls);
    }

    /// <summary>Re-pull one bill's latest version (detail, summaries, and text when tracked).</summary>
    public async Task<object?> RefreshOneAsync(string billId, CancellationToken ct = default)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<BillKeyRow>(
            """
            SELECT bill_id, congress, bill_type, bill_number, update_date::text AS update_date,
                   FALSE AS has_text, tracking_status
            FROM bills WHERE bill_id = @BillId
            """, new { BillId = billId });
        if (row is null) return null;

        var slug = await IngestDetailAsync(row.Congress, row.BillType, row.BillNumber,
                                           requireHousing: false, ct: ct);
        var textsPulled = false;
        if (row.TrackingStatus == "tracked")
        {
            await StoreTextsForAsync(billId, row.Congress, row.BillType, row.BillNumber, ct);
            textsPulled = true;
        }
        var hasSummary = await _dl.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM bill_summaries WHERE bill_id = @BillId)", new { BillId = billId });
        return new { bill_id = slug ?? billId, texts_pulled = textsPulled, has_summary = hasSummary };
    }

    /// <summary>Find housing bills updated in the window that are not yet on file. Stores nothing.</summary>
    public async Task<DiscoverResult> DiscoverNewAsync(int congress, int days, CancellationToken ct = default)
    {
        var fromDt = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var known = (await _dl.QueryAsync<string>(
            "SELECT bill_id FROM bills WHERE congress = @Congress", new { Congress = congress }))
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new List<Candidate>();
        int pages = 0, detailCalls = 0, listed = 0, offset = 0;

        while (pages < _opt.SyncMaxListPages && detailCalls < _opt.SyncMaxDetailCalls)
        {
            var pageJson = await _congress.FetchBillListPageAsync(congress, offset, 250, fromDt, ct);
            using var doc = JsonDocument.Parse(pageJson);
            var hasNext = doc.RootElement.TryGetProperty("pagination", out var pg) &&
                          pg.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String;
            if (!doc.RootElement.TryGetProperty("bills", out var arr) || arr.ValueKind != JsonValueKind.Array)
                break;
            pages++;

            foreach (var rowEl in arr.EnumerateArray())
            {
                listed++;
                if (detailCalls >= _opt.SyncMaxDetailCalls) break;
                var type = (rowEl.TryGetProperty("type", out var tEl) ? tEl.GetString() ?? "" : "").ToLowerInvariant();
                if (!rowEl.TryGetProperty("number", out var nEl)) continue;
                var number = nEl.ValueKind == JsonValueKind.Number ? nEl.GetInt32()
                    : int.TryParse(nEl.GetString(), out var n) ? n : 0;
                if (type.Length == 0 || number == 0) continue;
                var slug = BillRepository.BillSlug(congress, type, number);
                if (known.Contains(slug)) continue;

                detailCalls++;
                string billJson;
                try
                {
                    billJson = await _congress.FetchBillAsync(congress, type, number, refresh: true, ct);
                }
                catch (BillNotFoundException)
                {
                    continue;
                }
                var b = BillRepository.ParseBill(billJson);
                if (b.PolicyArea != TrackerRules.HousingPolicyArea) continue;

                var sponsors = BillRepository.ParseSponsors(billJson);
                // Provisional tags from title + latest action; the real scan runs
                // against the CRS summary when the bill is added.
                var tags = TrackerRules.DeriveTags(b.Title + " " + b.LatestActionText);
                candidates.Add(new Candidate(
                    congress, type, number, TrackerRules.FormatRef(type, number), b.Title,
                    b.OriginChamber, sponsors.FirstOrDefault()?.FullName,
                    b.IntroducedDate?.ToString("yyyy-MM-dd"),
                    b.LatestActionDate?.ToString("yyyy-MM-dd"), b.LatestActionText,
                    TrackerRules.StatusKey(b.LatestActionText), tags, TrackerRules.IsWatch(tags)));
            }

            if (!hasNext) break;
            offset += 250;
        }

        await SetSyncStateAsync("last_discovery_at", DateTime.UtcNow.ToString("o"));
        return new DiscoverResult("discover", days, listed, detailCalls, candidates);
    }

    /// <summary>Ingest one bill as tracked (with full text) or untracked (metadata only).</summary>
    public async Task<string?> AddBillAsync(int congress, string billType, int billNumber, bool tracked,
                                            CancellationToken ct = default)
    {
        // Discovery already cached the detail JSON on disk, so refresh:false is cheap.
        var slug = await IngestDetailAsync(congress, billType, billNumber,
                                           tracking: tracked ? "tracked" : "untracked",
                                           requireHousing: false, refresh: false, ct: ct);
        if (slug is not null && tracked)
            await StoreTextsForAsync(slug, congress, billType, billNumber, ct);
        return slug;
    }

    /// <summary>Flip tracked/untracked; pulls full text when tracking a bill that lacks it.</summary>
    public async Task<object?> SetTrackingAsync(string billId, bool tracked, CancellationToken ct = default)
    {
        var row = await _dl.QuerySingleOrDefaultAsync<BillKeyRow>(
            """
            SELECT bill_id, congress, bill_type, bill_number, NULL AS update_date,
                   EXISTS (SELECT 1 FROM bill_text_versions tv
                           WHERE tv.bill_id = bills.bill_id AND tv.text_content IS NOT NULL) AS has_text,
                   tracking_status
            FROM bills WHERE bill_id = @BillId
            """, new { BillId = billId });
        if (row is null) return null;

        var tracking = tracked ? "tracked" : "untracked";
        await _dl.ExecuteAsync("UPDATE bills SET tracking_status = @Tracking WHERE bill_id = @BillId",
                               new { Tracking = tracking, BillId = billId });
        var textsPulled = false;
        if (tracked && !row.HasText)
        {
            await StoreTextsForAsync(billId, row.Congress, row.BillType, row.BillNumber, ct);
            textsPulled = true;
        }
        return new { bill_id = billId, tracking_status = tracking, texts_pulled = textsPulled };
    }

    /// <summary>Publish (display_date = now) or unpublish (NULL).</summary>
    public async Task<object?> SetDisplayAsync(string billId, bool displayed)
    {
        var date = await _dl.QuerySingleOrDefaultAsync<DateTime?>(
            """
            UPDATE bills SET display_date = CASE WHEN @Displayed THEN now() ELSE NULL END
            WHERE bill_id = @BillId RETURNING display_date
            """, new { Displayed = displayed, BillId = billId });
        var exists = await _dl.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM bills WHERE bill_id = @BillId)", new { BillId = billId });
        if (!exists) return null;
        return new { bill_id = billId, display_date = date?.ToString("o") };
    }

    public async Task<object?> SetPinnedAsync(string billId, bool pinned)
    {
        var updated = await _dl.ExecuteAsync(
            "UPDATE bills SET pinned = @Pinned WHERE bill_id = @BillId",
            new { Pinned = pinned, BillId = billId });
        if (updated == 0) return null;
        return new { bill_id = billId, pinned };
    }

    public async Task<object> StatsAsync()
    {
        var rows = (await _dl.QueryAsync<(string TrackingStatus, long Bills, long WithText)>(
            """
            SELECT tracking_status,
                   count(*) AS bills,
                   count(*) FILTER (WHERE EXISTS (
                       SELECT 1 FROM bill_text_versions tv
                       WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL)) AS with_text
            FROM bills b WHERE policy_area = @PolicyArea
            GROUP BY tracking_status
            """, new { PolicyArea = TrackerRules.HousingPolicyArea })).ToList();

        object Bucket(string key)
        {
            var r = rows.FirstOrDefault(x => x.TrackingStatus == key);
            return new { bills = r.Bills, with_text = r.WithText };
        }
        return new
        {
            tracked = Bucket("tracked"),
            untracked = Bucket("untracked"),
            last_refresh_at = await GetSyncStateAsync("last_refresh_at"),
            last_discovery_at = await GetSyncStateAsync("last_discovery_at"),
        };
    }

    // --- the tracker feed ----------------------------------------------------

    private sealed class ListRow
    {
        public string BillId { get; set; } = "";
        public int Congress { get; set; }
        public string BillType { get; set; } = "";
        public int BillNumber { get; set; }
        public string? Title { get; set; }
        public string? OriginChamber { get; set; }
        public DateOnly? LatestActionDate { get; set; }
        public string? LatestActionText { get; set; }
        public DateOnly? IntroducedDate { get; set; }
        public string? PolicyArea { get; set; }
        public string TrackingStatus { get; set; } = "";
        public string[]? Tags { get; set; }
        public string? TagsSource { get; set; }
        public DateTime? DisplayDate { get; set; }
        public bool Pinned { get; set; }
        public string? SponsorName { get; set; }
        public string? SponsorParty { get; set; }
        public string? SponsorState { get; set; }
        public string? Summary { get; set; }
        public bool HasText { get; set; }
    }

    public async Task<List<TrackerListRow>> ListAsync(
        string view, string tracking, string? q, int? congress, int limit,
        string? policyArea = TrackerRules.HousingPolicyArea)
    {
        var sql = """
            SELECT b.bill_id, b.congress, b.bill_type, b.bill_number, b.title,
                   b.origin_chamber, b.latest_action_date, b.latest_action_text,
                   b.introduced_date, b.policy_area, b.tracking_status, b.tags, b.tags_source,
                   b.display_date, b.pinned,
                   sp.full_name AS sponsor_name, sp.party AS sponsor_party, sp.state AS sponsor_state,
                   sm.text AS summary,
                   EXISTS (SELECT 1 FROM bill_text_versions tv
                           WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL) AS has_text
            FROM bills b
            LEFT JOIN LATERAL (
                SELECT full_name, party, state FROM bill_sponsors WHERE bill_id = b.bill_id LIMIT 1
            ) sp ON TRUE
            LEFT JOIN LATERAL (
                SELECT text FROM bill_summaries WHERE bill_id = b.bill_id
                ORDER BY action_date DESC NULLS LAST LIMIT 1
            ) sm ON TRUE
            WHERE TRUE
            """;

        var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(policyArea))
        {
            sql += " AND b.policy_area = @PolicyArea";
            p.Add("PolicyArea", policyArea);
        }
        if (view == "public")
        {
            sql += " AND b.display_date IS NOT NULL AND b.display_date <= now()";
        }
        else if (tracking is "tracked" or "untracked")
        {
            sql += " AND b.tracking_status = @Tracking";
            p.Add("Tracking", tracking);
        }
        if (congress is not null)
        {
            sql += " AND b.congress = @Congress";
            p.Add("Congress", congress);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += """
                 AND (b.title ILIKE @Like OR b.bill_id ILIKE @Like OR sp.full_name ILIKE @Like
                      OR sm.text ILIKE @Like OR b.latest_action_text ILIKE @Like)
                """;
            p.Add("Like", $"%{q.Trim()}%");
        }
        sql += view == "public"
            ? " ORDER BY b.pinned DESC, b.latest_action_date DESC NULLS LAST LIMIT @Limit"
            : " ORDER BY b.latest_action_date DESC NULLS LAST LIMIT @Limit";
        p.Add("Limit", limit);

        var rows = await _dl.QueryAsync<ListRow>(sql, p);
        var now = DateTime.UtcNow;
        return rows.Select(r => new TrackerListRow(
            r.BillId, r.TrackingStatus, r.HasText, r.Tags ?? Array.Empty<string>(), r.TagsSource,
            TrackerRules.IsWatch(r.Tags), r.DisplayDate?.ToString("o"),
            r.DisplayDate is not null && r.DisplayDate <= now, r.Pinned,
            TrackerRules.FormatRef(r.BillType, r.BillNumber), TrackerRules.CongressOrdinal(r.Congress),
            r.OriginChamber, r.Title, TrackerRules.StatusKey(r.LatestActionText), r.LatestActionText,
            r.LatestActionDate?.ToString("yyyy-MM-dd"), r.IntroducedDate?.ToString("yyyy-MM-dd"),
            r.PolicyArea, r.SponsorName, r.SponsorParty, r.SponsorState,
            string.IsNullOrEmpty(r.Summary) ? null : TrackerRules.StripHtml(r.Summary),
            TrackerRules.CongressGovUrl(r.Congress, r.BillType, r.BillNumber))).ToList();
    }

    // --- sync state ----------------------------------------------------------

    public Task<string?> GetSyncStateAsync(string key) =>
        _dl.QuerySingleOrDefaultAsync<string>("SELECT value FROM sync_state WHERE key = @Key", new { Key = key });

    public Task SetSyncStateAsync(string key, string value) =>
        _dl.ExecuteAsync(
            """
            INSERT INTO sync_state (key, value, updated_at) VALUES (@Key, @Value, now())
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()
            """, new { Key = key, Value = value });
}
