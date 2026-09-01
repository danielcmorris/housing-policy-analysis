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

    public Task<GeminiResult> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct) =>
        GenerateChatAsync(systemInstruction, new[] { ("user", userPrompt) }, ct);

    /// <summary>Multi-turn generation. Roles are 'user' or 'model'.</summary>
    public Task<GeminiResult> GenerateChatAsync(
        string systemInstruction, IReadOnlyList<(string Role, string Text)> turns, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = turns.Select(t => new
            {
                role = t.Role == "model" ? "model" : "user",
                parts = new[] { new { text = t.Text } },
            }),
            // thinkingBudget 0: on Gemini 2.5, thinking tokens count against
            // maxOutputTokens and can starve the visible answer.
            generationConfig = new
            {
                maxOutputTokens = _opt.MaxOutputTokens,
                temperature = 0.2,
                thinkingConfig = new { thinkingBudget = 0 },
            },
        });
        return CallAsync(_opt.Model, body, ct);
    }

    /// <summary>
    /// Convert a PDF to Markdown by sending the document itself (Gemini reads
    /// PDFs natively, so layout/tables survive). Runs on the cheaper
    /// PdfMarkdownModel with thinking off; the caller computes and passes the
    /// output-token cap and does the pre-call page/token assessment.
    /// </summary>
    public Task<GeminiResult> ConvertPdfToMarkdownAsync(
        byte[] pdfBytes, int maxOutputTokens, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inlineData = new { mimeType = "application/pdf",
                                                 data = Convert.ToBase64String(pdfBytes) } },
                        new { text = "Convert this document to clean, well-structured Markdown. " +
                                     "Use # headings matching the document's structure, bullet/numbered " +
                                     "lists, and Markdown tables for tabular data. Transcribe the text " +
                                     "faithfully — fix hyphenation across line breaks. OMIT entirely: " +
                                     "the table of contents, page headers, page footers, and page " +
                                     "numbers. Never output dot leaders or any run of repeated filler " +
                                     "characters (....., -----, etc.). Describe charts/figures in one " +
                                     "italic line. Output only the Markdown." },
                    },
                },
            },
            generationConfig = new
            {
                maxOutputTokens,
                temperature = 0.1,
                thinkingConfig = new { thinkingBudget = 0 },
            },
        });
        return CallAsync(_opt.PdfMarkdownModel, body, ct);
    }

    private async Task<GeminiResult> CallAsync(string model, string body, CancellationToken ct)
    {
        if (_credentialsPath is null)
            throw new InvalidOperationException(
                "Gemini service-account key not found under creds/ — see GeminiOptions.CredentialsFile.");

        var credential = GoogleCredential.FromFile(_credentialsPath)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

        var url = $"https://{_opt.Location}-aiplatform.googleapis.com/v1/projects/{_opt.ProjectId}" +
                  $"/locations/{_opt.Location}/publishers/google/models/{model}:generateContent";

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
