using Dapper;
using HousingPolicy.Api.Json;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using HousingPolicy.Api.Services;

// Map snake_case Postgres columns to PascalCase properties without per-query
// AS aliases (house convention — see mypfsa/pfsa-api Program.cs).
DefaultTypeMap.MatchNamesWithUnderscores = true;

// Teach Dapper how to bind DateOnly / DateOnly? to Npgsql `date`.
SqlMapper.AddTypeHandler(new DateOnlyHandler());
SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());

var builder = WebApplication.CreateBuilder(args);

// Secrets live in the gitignored creds/ folder (the @creds location in CLAUDE.md).
// Layer creds/config.json under environment variables so env still wins, then
// re-add env vars to restore that precedence: env > creds/config.json > appsettings.
var credsPath = FindCredsConfig(builder.Environment.ContentRootPath);
if (credsPath is not null)
{
    builder.Configuration.AddJsonFile(credsPath, optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
    Console.WriteLine($"Loaded creds config: {credsPath}");
}

builder.Services.Configure<CongressOptions>(builder.Configuration.GetSection(CongressOptions.SectionName));
builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection(TrackerOptions.SectionName));
builder.Services.Configure<StudiesOptions>(builder.Configuration.GetSection(StudiesOptions.SectionName));
builder.Services.Configure<CityOptions>(builder.Configuration.GetSection(CityOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));

builder.Services.AddScoped<DataLayerBase>();
builder.Services.AddScoped<BillRepository>();
builder.Services.AddScoped<SchemaInitializer>();
builder.Services.AddScoped<TrackerService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<StudyService>();
builder.Services.AddScoped<ExpertService>();
builder.Services.AddScoped<CityService>();
builder.Services.AddScoped<DocumentRegistryService>();

// Typed congress.gov client over HttpClientFactory-managed handlers.
var congressOpt = builder.Configuration.GetSection(CongressOptions.SectionName).Get<CongressOptions>() ?? new CongressOptions();
builder.Services.AddHttpClient<CongressClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(congressOpt.HttpTimeoutSeconds);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("housing-policy-laws/0.1");
});

// Typed Legistar client for the city-legislation sync.
builder.Services.AddHttpClient<LegistarClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(congressOpt.HttpTimeoutSeconds);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("housing-policy-laws/0.1");
});

// RAG: local Ollama embeddings + Vertex Gemini synthesis.
var ollamaOpt = builder.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();
builder.Services.AddHttpClient<OllamaEmbedClient>(c =>
    c.Timeout = TimeSpan.FromSeconds(ollamaOpt.TimeoutSeconds));
builder.Services.AddHttpClient<GeminiClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient<VertexEmbedClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<AssistantService>();

// Angular front end (dev + house origins).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200",
                           "http://localhost:4300", "http://127.0.0.1:4300")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// snake_case JSON to match the Postgres columns and the Angular client's
// existing payload shapes (bill_id, tracking_status, status_key, ...).
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply schema.sql (idempotent) + ensure the congress_gov source row on boot.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SchemaInitializer>().InitAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Deployment hosting: serve the Angular build from wwwroot when present,
// with an SPA fallback so deep links land on index.html. No-ops in dev,
// where there is no wwwroot and ng serve owns the front end.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
    app.MapFallbackToFile("index.html");

app.Run();

// Walk up from the content root to find creds/config.json (repo root/creds).
static string? FindCredsConfig(string start)
{
    var dir = new DirectoryInfo(start);
    for (var i = 0; i < 6 && dir is not null; i++)
    {
        var candidate = Path.Combine(dir.FullName, "creds", "config.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}

// Exposed so a future test project can drive the app via WebApplicationFactory.
public partial class Program { }
