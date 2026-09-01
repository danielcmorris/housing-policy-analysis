namespace HousingPolicy.Api.Options;

/// <summary>
/// Local Ollama server (embeddings — and later, local generation). The embed
/// model here MUST match api/embed_chunks.py: query vectors are only
/// comparable to document vectors from the same model.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://192.168.168.200:11434";
    public string EmbedModel { get; set; } = "nomic-embed-text";
    public int EmbedDimensions { get; set; } = 768;
    public double TimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Embedding provider selection + Vertex embedding settings. Documents and
/// queries MUST embed with the same model; the provider/model here governs
/// both sides. Vertex text-embedding-004 emits 768 dims, matching the
/// document_chunks.embedding column.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>'vertex' (Gemini) or 'ollama' (local).</summary>
    public string Provider { get; set; } = "vertex";

    public string VertexModel { get; set; } = "text-embedding-004";

    /// <summary>Texts per Vertex predict call.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Hard cap on chunks embedded in one admin run.</summary>
    public int MaxChunksPerRun { get; set; } = 2000;
}

/// <summary>
/// Vertex AI Gemini (answer synthesis). Service-account auth only, per
/// project rules — the key file lives in the gitignored creds/ folder. Hard
/// token limits are enforced BEFORE every call (token assessment), and every
/// call is recorded in ai_usage (token tracking).
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ProjectId { get; set; } = "morrisdev-203721";
    public string Location { get; set; } = "us-central1";
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>Key file name searched for under the repo creds/ folder.</summary>
    public string CredentialsFile { get; set; } = "gemini-service-account.json";

    /// <summary>Hard cap on estimated input tokens per call (assessed pre-call).</summary>
    public int MaxInputTokens { get; set; } = 12000;

    /// <summary>Hard cap on generated tokens per call.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Most retrieved chunks ever handed to the model.</summary>
    public int MaxContextChunks { get; set; } = 8;

    /// <summary>
    /// Hard cap for document-scoped chat, where the ENTIRE document text
    /// rides in the context window (well within Gemini's window; documents
    /// beyond this are truncated with a visible marker).
    /// </summary>
    public int MaxChatInputTokens { get; set; } = 250000;

    // --- PDF → Markdown conversion (admin studies editor) -------------------

    /// <summary>
    /// Model for PDF→Markdown conversion. Transcription-style work, so the
    /// lite tier is fine and its output rate is ~6× cheaper than flash
    /// (output dominates this call's cost ~25:1).
    /// </summary>
    public string PdfMarkdownModel { get; set; } = "gemini-2.5-flash-lite";

    /// <summary>Hard cap on PDF pages accepted for a markdown conversion.
    /// Vertex bills PDF input flat at ~258 tokens/page, so this also caps
    /// input at ~258 × pages (100 pages ≈ 26k input tokens ≈ $0.003).</summary>
    public int PdfMarkdownMaxPages { get; set; } = 100;

    /// <summary>Output-token budget granted per PDF page (~800 covers a dense
    /// text page); pages × this, clamped below, is the run's output cap.</summary>
    public int PdfMarkdownOutputTokensPerPage { get; set; } = 800;

    /// <summary>Absolute output-token ceiling per conversion run — the worst
    /// possible bill per document is known in advance (~$0.026 at lite rates).</summary>
    public int PdfMarkdownMaxOutputTokens { get; set; } = 65000;
}
