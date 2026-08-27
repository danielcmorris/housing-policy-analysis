using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Text embeddings from Vertex AI (text-embedding-004, 768 dims), using the
/// same service-account auth as GeminiClient. The predict response reports an
/// exact token_count per text — returned to callers so embedding runs are
/// costed precisely in the ai_usage ledger.
/// </summary>
public sealed class VertexEmbedClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _gemini;
    private readonly EmbeddingOptions _opt;
    private readonly string? _credentialsPath;

    public sealed record EmbedBatch(List<float[]> Vectors, int TotalTokens, bool AnyTruncated);

    public VertexEmbedClient(HttpClient http, IOptions<GeminiOptions> gemini,
                             IOptions<EmbeddingOptions> options, IHostEnvironment env)
    {
        _http = http;
        _gemini = gemini.Value;
        _opt = options.Value;
        _credentialsPath = FindCredentials(env.ContentRootPath, _gemini.CredentialsFile);
    }

    public bool IsConfigured => _credentialsPath is not null;
    public string Model => _opt.VertexModel;

    /// <summary>Embed one query string (for search); null on any failure.</summary>
    public async Task<float[]?> EmbedQueryAsync(string text, CancellationToken ct)
    {
        try
        {
            var batch = await EmbedAsync(new[] { text }, "RETRIEVAL_QUERY", ct);
            return batch.Vectors.Count == 1 ? batch.Vectors[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Embed a batch of document chunks; throws on failure (callers stop the run).</summary>
    public Task<EmbedBatch> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        EmbedAsync(texts, "RETRIEVAL_DOCUMENT", ct);

    private async Task<EmbedBatch> EmbedAsync(IReadOnlyList<string> texts, string taskType, CancellationToken ct)
    {
        if (_credentialsPath is null)
            throw new InvalidOperationException("Gemini service-account key not found under creds/.");

        var credential = GoogleCredential.FromFile(_credentialsPath)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

        var url = $"https://{_gemini.Location}-aiplatform.googleapis.com/v1/projects/{_gemini.ProjectId}" +
                  $"/locations/{_gemini.Location}/publishers/google/models/{_opt.VertexModel}:predict";

        var body = JsonSerializer.Serialize(new
        {
            instances = texts.Select(t => new { task_type = taskType, content = t }),
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
            throw new InvalidOperationException($"Vertex embedding HTTP {(int)resp.StatusCode}: {snippet}");
        }

        using var doc = JsonDocument.Parse(payload);
        var vectors = new List<float[]>();
        var totalTokens = 0;
        var truncated = false;
        foreach (var pred in doc.RootElement.GetProperty("predictions").EnumerateArray())
        {
            var emb = pred.GetProperty("embeddings");
            vectors.Add(emb.GetProperty("values").EnumerateArray().Select(v => v.GetSingle()).ToArray());
            if (emb.TryGetProperty("statistics", out var stats))
            {
                if (stats.TryGetProperty("token_count", out var tc)) totalTokens += tc.GetInt32();
                if (stats.TryGetProperty("truncated", out var tr) && tr.GetBoolean()) truncated = true;
            }
        }
        if (vectors.Count != texts.Count)
            throw new InvalidOperationException($"expected {texts.Count} embeddings, got {vectors.Count}");
        return new EmbedBatch(vectors, totalTokens, truncated);
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
