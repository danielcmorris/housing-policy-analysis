using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Vertex AI Gemini via REST, authenticated with the dedicated service
/// account (never an API key, per project rules). The caller (SearchService)
/// performs the pre-call token assessment; this client enforces the output
/// cap and reports usage metadata for the ai_usage ledger.
/// </summary>
public sealed class GeminiClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _opt;
    private readonly string? _credentialsPath;

    public sealed record GeminiResult(string Text, int InputTokens, int OutputTokens);

    public GeminiClient(HttpClient http, IOptions<GeminiOptions> options, IHostEnvironment env)
    {
        _http = http;
        _opt = options.Value;
        _credentialsPath = FindCredentials(env.ContentRootPath, _opt.CredentialsFile);
    }

    public bool IsConfigured => _credentialsPath is not null;

    public async Task<GeminiResult> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct)
    {
        if (_credentialsPath is null)
            throw new InvalidOperationException(
                "Gemini service-account key not found under creds/ — see GeminiOptions.CredentialsFile.");

        var credential = GoogleCredential.FromFile(_credentialsPath)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

        var url = $"https://{_opt.Location}-aiplatform.googleapis.com/v1/projects/{_opt.ProjectId}" +
                  $"/locations/{_opt.Location}/publishers/google/models/{_opt.Model}:generateContent";

        var body = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { maxOutputTokens = _opt.MaxOutputTokens, temperature = 0.2 },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var snippet = payload.Length > 300 ? payload[..300] : payload;
            throw new InvalidOperationException($"Vertex Gemini HTTP {(int)resp.StatusCode}: {snippet}");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var text = "";
        if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0 &&
            cands[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts))
            text = string.Concat(parts.EnumerateArray()
                .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(s => s is not null));

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var ot)) outputTokens = ot.GetInt32();
        }
        return new GeminiResult(text, inputTokens, outputTokens);
    }

    private static string? FindCredentials(string start, string fileName)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "creds", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
