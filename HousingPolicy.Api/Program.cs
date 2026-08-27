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

builder.Services.AddScoped<DataLayerBase>();
builder.Services.AddScoped<BillRepository>();
builder.Services.AddScoped<SchemaInitializer>();

// Typed congress.gov client over HttpClientFactory-managed handlers.
var congressOpt = builder.Configuration.GetSection(CongressOptions.SectionName).Get<CongressOptions>() ?? new CongressOptions();
builder.Services.AddHttpClient<CongressClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(congressOpt.HttpTimeoutSeconds);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("housing-policy-laws/0.1");
});

// Angular front end (dev + house origins).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();
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
app.MapControllers();

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
