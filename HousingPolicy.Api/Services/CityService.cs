using System.Globalization;
using System.Text.Json;
using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// City legislation sync — the municipal counterpart of TrackerService.
/// Walks a Legistar client's recently-modified matters, keeps housing-relevant
/// ordinances/resolutions (keyword taxonomy on the title — Legistar has no CRS
/// policy area), stores them with the same curation model as federal bills,
/// pulls matter text, and registers each stored matter in the unified
/// document registry for RAG.
/// </summary>
public sealed class CityService
{
    private readonly DataLayerBase _dl;
    private readonly LegistarClient _legistar;
    private readonly DocumentRegistryService _registry;
    private readonly CityOptions _opt;
    private readonly TrackerOptions _caps;
    private readonly ILogger<CityService> _log;

    public CityService(DataLayerBase dl, LegistarClient legistar, DocumentRegistryService registry,
                       IOptions<CityOptions> options, IOptions<TrackerOptions> caps,
                       ILogger<CityService> log)
    {
        _dl = dl;
        _legistar = legistar;
        _registry = registry;
        _opt = options.Value;
        _caps = caps.Value;
        _log = log;
    }

    public sealed record SyncResult(string Client, int Listed, int Stored, int TextsPulled, List<string> StoredIds);

    /// <summary>Sync one configured city: walk matters modified in the window, keep housing-relevant ones.</summary>
    public async Task<SyncResult?> SyncAsync(string clientKey, int days, CancellationToken ct = default)
    {
        var city = _opt.Clients.FirstOrDefault(c => c.Key == clientKey);
        if (city is null) return null;

        var since = DateTime.UtcNow.AddDays(-days);
        var types = _opt.MatterTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listed = 0;
        var texts = 0;
        var stored = new List<string>();
        var skip = 0;
        const int pageSize = 250;

        for (var page = 0; page < _caps.SyncMaxListPages && stored.Count < _caps.SyncMaxDetailCalls; page++)
        {
            var rows = await _legistar.FetchMattersPageAsync(city.Key, skip, pageSize, since, ct);
            if (rows.Count == 0) break;
            listed += rows.Count;
            _log.LogInformation("city sync {Client}: page {Page}, {Rows} rows", city.Key, page, rows.Count);

            foreach (var m in rows)
            {
                if (stored.Count >= _caps.SyncMaxDetailCalls) break;
                var type = GetString(m, "MatterTypeName") ?? "";
                if (!types.Contains(type)) continue;
                var title = GetString(m, "MatterTitle") ?? GetString(m, "MatterName") ?? "";
                // Housing relevance: same keyword taxonomy the federal tagger
                // uses; a matter with no housing tag is skipped.
                var tags = TrackerRules.DeriveTags(title);
                if (tags.Length == 0) continue;

                var matterId = GetInt(m, "MatterId");
                if (matterId is null) continue;

                var id = $"{city.Key}-{matterId}";
                _log.LogInformation("city sync {Client}: fetching text for {Id}", city.Key, id);
                var text = await _legistar.FetchMatterTextAsync(city.Key, matterId.Value, ct);
                if (text is not null) texts++;

                _log.LogInformation("city sync {Client}: storing {Id}", city.Key, id);
                await StoreMatterAsync(city, m, matterId.Value, id, title, type, tags, text, ct);
                _log.LogInformation("city sync {Client}: registering {Id}", city.Key, id);
                await _registry.UpsertCityMatterAsync(id, ct);
                stored.Add(id);
            }
            skip += pageSize;
        }
        return new SyncResult(city.Key, listed, stored.Count, texts, stored);
    }

    private async Task StoreMatterAsync(
        CityOptions.CityClient city, JsonElement m, int matterId, string id,
        string title, string type, string[] tags, string? text, CancellationToken ct)
    {
        await _dl.ExecuteAsync(
            """
            INSERT INTO city_matters (city_matter_id, client, city_name, matter_id, matter_file,
                                      matter_type, title, matter_name, status, body_name,
                                      intro_date, agenda_date, passed_date, enactment_number,
                                      last_modified, text_content, tags, tags_source, data_vintage)
            VALUES (@Id, @Client, @CityName, @MatterId, @File, @Type, @Title, @Name, @Status, @Body,
                    @IntroDate, @AgendaDate, @PassedDate, @Enactment,
                    @LastModified, @Text, @Tags, 'title', @Vintage)
            ON CONFLICT (city_matter_id) DO UPDATE SET
                matter_file = EXCLUDED.matter_file, matter_type = EXCLUDED.matter_type,
                title = EXCLUDED.title, matter_name = EXCLUDED.matter_name,
                status = EXCLUDED.status, body_name = EXCLUDED.body_name,
                intro_date = EXCLUDED.intro_date, agenda_date = EXCLUDED.agenda_date,
                passed_date = EXCLUDED.passed_date, enactment_number = EXCLUDED.enactment_number,
                last_modified = EXCLUDED.last_modified,
                text_content = COALESCE(EXCLUDED.text_content, city_matters.text_content),
                data_vintage = EXCLUDED.data_vintage
            """,
            new
            {
                Id = id, Client = city.Key, CityName = city.Name, MatterId = matterId,
                File = GetString(m, "MatterFile"), Type = type, Title = title,
                Name = GetString(m, "MatterName"), Status = GetString(m, "MatterStatusName"),
                Body = GetString(m, "MatterBodyName"),
                IntroDate = GetDate(m, "MatterIntroDate"),
                AgendaDate = GetDate(m, "MatterAgendaDate"),
                PassedDate = GetDate(m, "MatterPassedDate"),
                Enactment = GetString(m, "MatterEnactmentNumber"),
                LastModified = GetTimestamp(m, "MatterLastModifiedUtc"),
                Text = text, Tags = tags, Vintage = DateTime.UtcNow,
            });
    }

    public sealed class CityMatterRow
    {
        public string CityMatterId { get; set; } = "";
        public string Client { get; set; } = "";
        public string? CityName { get; set; }
        public string? MatterFile { get; set; }
        public string? MatterType { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? BodyName { get; set; }
        public DateOnly? IntroDate { get; set; }
        public DateOnly? PassedDate { get; set; }
        public string TrackingStatus { get; set; } = "tracked";
        public string[]? Tags { get; set; }
        public DateTime? DisplayDate { get; set; }
        public bool Pinned { get; set; }
        public bool HasText { get; set; }
    }

    public async Task<List<CityMatterRow>> ListAsync(string view, string? client, string? q, int limit)
    {
        var sql = """
            SELECT city_matter_id, client, city_name, matter_file, matter_type, title, status,
                   body_name, intro_date, passed_date, tracking_status, tags, display_date,
                   pinned, (text_content IS NOT NULL) AS has_text
            FROM city_matters WHERE TRUE
            """;
        var p = new DynamicParameters();
        if (view == "public")
            sql += " AND display_date IS NOT NULL AND display_date <= now()";
        if (!string.IsNullOrEmpty(client))
        {
            sql += " AND client = @Client";
            p.Add("Client", client);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (title ILIKE @Like OR matter_file ILIKE @Like OR text_content ILIKE @Like)";
            p.Add("Like", $"%{q.Trim()}%");
        }
        sql += " ORDER BY pinned DESC, intro_date DESC NULLS LAST LIMIT @Limit";
        p.Add("Limit", limit);
        return (await _dl.QueryAsync<CityMatterRow>(sql, p)).ToList();
    }

    // --- json helpers (Legistar payloads) ------------------------------------

    private static string? GetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static DateOnly? GetDate(JsonElement e, string prop)
    {
        var s = GetString(e, prop);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? DateOnly.FromDateTime(dt) : null;
    }

    private static DateTime? GetTimestamp(JsonElement e, string prop)
    {
        var s = GetString(e, prop);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                 DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : null;
    }
}
