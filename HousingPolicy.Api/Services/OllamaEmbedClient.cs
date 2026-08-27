using System.Text;
using System.Text.Json;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Query embeddings from the local Ollama server. Returns null when the
/// server is unreachable or the response is malformed — the search layer then
/// falls back to keyword ranking instead of failing.
/// </summary>
public sealed class OllamaEmbedClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _opt;
    private readonly ILogger<OllamaEmbedClient> _log;

    public OllamaEmbedClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaEmbedClient> log)
    {
        _http = http;
        _opt = options.Value;
        _log = log;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { model = _opt.EmbedModel, input = text });
            using var resp = await _http.PostAsync(
                $"{_opt.BaseUrl.TrimEnd('/')}/api/embed",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("ollama embed returned {Status}", (int)resp.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("embeddings", out var arr) ||
                arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return null;
            var vec = arr[0].EnumerateArray().Select(v => v.GetSingle()).ToArray();
            return vec.Length == _opt.EmbedDimensions ? vec : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning("ollama embed unavailable: {Message}", ex.Message);
            return null;
        }
    }
}
