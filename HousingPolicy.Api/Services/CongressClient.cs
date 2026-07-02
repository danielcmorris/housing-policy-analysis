using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Thin async client for api.congress.gov v3. Its job is narrow on purpose
/// (same split as the Python congress_client.py): fetch raw JSON / text and
/// return it; all shaping into the schema lives in <see cref="BillRepository"/>.
///
/// Adds what a keyed, rate-limited API needs: retry with exponential backoff
/// (honoring Retry-After), typed errors, and api_key redaction so the secret
/// never reaches a log, exception, or cached file.
/// </summary>
public sealed class CongressClient
{
    private readonly HttpClient _http;
    private readonly CongressOptions _opt;
    private readonly ILogger<CongressClient> _log;
    private readonly string _baseUrl;
    private readonly string _dataDir;

    private static readonly Regex ApiKeyPattern =
        new(@"api_key=[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Govinfo filename suffix -> version code, e.g. ".../BILLS-119hr6644enr.htm" -> "enr".
    private static readonly Regex VersionCodePattern =
        new(@"BILLS-\d+[a-z]+\d+([a-z]+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CongressClient(
        HttpClient http,
        IOptions<CongressOptions> options,
        IHostEnvironment env,
        ILogger<CongressClient> log)
    {
        _http = http;
        _opt = options.Value;
        _log = log;
        _baseUrl = _opt.BaseUrl.TrimEnd('/');
        _dataDir = Path.IsPathRooted(_opt.DataDir)
            ? _opt.DataDir
            : Path.Combine(env.ContentRootPath, _opt.DataDir);
    }

    /// <summary>Mask any api_key query value so the secret never lands in a log/error/URL.</summary>
    public static string Redact(string url) => ApiKeyPattern.Replace(url, "api_key=REDACTED");

    /// <summary>Version code from a govinfo URL, else a slug of the version name. Never empty (part of a PK).</summary>
    public static string VersionCode(string? name, string? url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            var m = VersionCodePattern.Match(url);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        }
        var slug = Regex.Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9]+", "");
        if (slug.Length > 20) slug = slug[..20];
        return slug.Length > 0 ? slug : "unknown";
    }

    // --- public fetch surface ------------------------------------------------

    private static readonly (string, string)[] JsonOnly = { ("format", "json") };

    public Task<string> FetchBillAsync(int congress, string billType, int billNumber, bool refresh, CancellationToken ct)
    {
        var url = $"{_baseUrl}/bill/{congress}/{billType}/{billNumber}";
        var cache = Path.Combine(_dataDir, $"bill_{congress}_{billType}_{billNumber}.json");
        return GetJsonAsync(url, JsonOnly, cache, refresh, ct);
    }

    public Task<string> FetchBillTextAsync(int congress, string billType, int billNumber, bool refresh, CancellationToken ct)
    {
        var url = $"{_baseUrl}/bill/{congress}/{billType}/{billNumber}/text";
        var cache = Path.Combine(_dataDir, $"bill_{congress}_{billType}_{billNumber}_text.json");
        return GetJsonAsync(url, JsonOnly, cache, refresh, ct);
    }

    /// <summary>
    /// Fetch all pages of a bill sub-resource (cosponsors, amendments, actions,
    /// committees, summaries, subjects, titles, relatedbills). Returns the raw
    /// JSON of each page; congress.gov paginates via ?offset/&limit and reports
    /// the total in pagination.count. One page covers most bills (limit 250).
    /// </summary>
    public async Task<List<string>> FetchSubResourcePagesAsync(
        int congress, string billType, int billNumber, string resource, bool refresh, CancellationToken ct)
    {
        const int limit = 250;
        var pages = new List<string>();
        var offset = 0;
        for (var guard = 0; guard < 40; guard++)   // hard cap against runaway pagination
        {
            var url = $"{_baseUrl}/bill/{congress}/{billType}/{billNumber}/{resource}";
            var cache = Path.Combine(_dataDir,
                $"bill_{congress}_{billType}_{billNumber}_{resource}_{offset}.json");
            var body = await GetJsonAsync(url,
                new[] { ("format", "json"), ("limit", limit.ToString()), ("offset", offset.ToString()) },
                cache, refresh, ct);
            pages.Add(body);

            int total;
            using (var doc = JsonDocument.Parse(body))
                total = doc.RootElement.TryGetProperty("pagination", out var p) &&
                        p.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt32() : 0;

            offset += limit;
            if (offset >= total) break;
        }
        return pages;
    }

    /// <summary>Fetch a text-version body (public congress.gov/govinfo URL, no api_key).</summary>
    public async Task<string> FetchTextBodyAsync(string url, CancellationToken ct)
    {
        using var resp = await RequestAsync(url, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    // --- JSON endpoints with raw-zone disk cache -----------------------------

    private async Task<string> GetJsonAsync(
        string url, IReadOnlyList<(string Key, string Value)> query, string cachePath, bool refresh, CancellationToken ct)
    {
        if (!refresh && File.Exists(cachePath))
            return await File.ReadAllTextAsync(cachePath, ct);

        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var full = $"{url}?{qs}&api_key={Uri.EscapeDataString(_opt.ApiKey)}";
        using var resp = await RequestAsync(full, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, body, ct);
        return body;
    }

    // --- low-level request with retry ----------------------------------------

    /// <summary>GET with retry on timeout / transport error / 429 / 5xx. 404 raises immediately.</summary>
    private async Task<HttpResponseMessage> RequestAsync(string url, CancellationToken ct)
    {
        Exception? lastExc = null;
        var last429 = false;

        for (var attempt = 0; attempt <= _opt.HttpRetries; attempt++)
        {
            HttpResponseMessage? resp = null;
            try
            {
                resp = await _http.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // transport error / timeout -> retryable
                lastExc = ex;
                last429 = false;
            }

            if (resp is not null)
            {
                var status = (int)resp.StatusCode;
                if (status == 404)
                {
                    resp.Dispose();
                    throw new BillNotFoundException($"not found: {Redact(url)}");
                }
                if (status < 400)
                    return resp;

                if (status == 429 || status >= 500)
                {
                    lastExc = new CongressApiException($"HTTP {status} from {Redact(url)}");
                    last429 = status == 429;
                    await SleepBeforeRetryAsync(attempt, _opt.HttpRetries, resp, ct);
                    resp.Dispose();
                    continue;
                }

                // other 4xx: permanent
                var text = await resp.Content.ReadAsStringAsync(ct);
                resp.Dispose();
                var snippet = text.Length > 200 ? text[..200] : text;
                throw new CongressApiException($"HTTP {status} from {Redact(url)}: {snippet}");
            }

            await SleepBeforeRetryAsync(attempt, _opt.HttpRetries, null, ct);
        }

        if (last429)
            throw new RateLimitedException($"rate limited after {_opt.HttpRetries} retries: {lastExc?.Message}");
        throw new CongressApiException($"request failed after {_opt.HttpRetries} retries: {lastExc?.Message}", lastExc!);
    }

    private static async Task SleepBeforeRetryAsync(int attempt, int retries, HttpResponseMessage? resp, CancellationToken ct)
    {
        // Don't sleep after the final attempt (matches the Python guard).
        if (attempt >= retries) return;

        var backoff = 0.5 * Math.Pow(2, attempt);
        var retryAfter = resp?.Headers.RetryAfter?.Delta?.TotalSeconds;
        if (retryAfter is > 0)
            backoff = Math.Max(backoff, retryAfter.Value);
        await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
    }
}
