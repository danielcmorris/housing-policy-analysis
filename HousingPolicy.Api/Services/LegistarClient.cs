using System.Text.Json;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Thin client for the Granicus Legistar Web API (the municipal analogue of
/// CongressClient). Free, keyless, OData-flavored REST. Quirks handled here:
/// the filter dialect is partial (no tolower()), sub-resources key on the
/// internal MatterId (not the human file number), and some matters return
/// malformed sub-resources — callers treat those as absent.
/// </summary>
public sealed class LegistarClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public LegistarClient(HttpClient http, IOptions<CityOptions> options)
    {
        _http = http;
        _baseUrl = options.Value.LegistarBaseUrl.TrimEnd('/');
    }

    /// <summary>One page of matters for a client, most recently modified first.</summary>
    public async Task<List<JsonElement>> FetchMattersPageAsync(
        string client, int skip, int top, DateTime? modifiedSince, CancellationToken ct)
    {
        var filter = modifiedSince is { } dt
            ? $"&$filter=MatterLastModifiedUtc%20gt%20datetime'{dt:yyyy-MM-ddTHH:mm:ss}'"
            : "";
        var url = $"{_baseUrl}/{client}/matters?$top={top}&$skip={skip}" +
                  $"&$orderby=MatterLastModifiedUtc%20desc{filter}";
        return await GetArrayAsync(url, ct);
    }

    /// <summary>Plain text of a matter's current version, or null when absent/malformed.</summary>
    public async Task<string?> FetchMatterTextAsync(string client, int matterId, CancellationToken ct)
    {
        // /texts lists versions; each needs a second call for the body. The
        // versions list is small; take the last (most recent) version id.
        List<JsonElement> versions;
        try
        {
            versions = await GetArrayAsync($"{_baseUrl}/{client}/matters/{matterId}/versions", ct);
        }
        catch (CongressApiException)
        {
            return null;
        }
        if (versions.Count == 0) return null;
        var last = versions[^1];
        if (!last.TryGetProperty("Key", out var key)) return null;

        try
        {
            var body = await GetStringAsync(
                $"{_baseUrl}/{client}/matters/{matterId}/texts/{key.GetString()}", ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("MatterTextPlain", out var t) &&
                   t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
        }
        catch (CongressApiException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<JsonElement>> GetArrayAsync(string url, CancellationToken ct)
    {
        var body = await GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new CongressApiException($"non-array response from {url}");
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new CongressApiException($"HTTP {(int)resp.StatusCode} from {url}: {body[..Math.Min(120, body.Length)]}");
        return body;
    }
}
