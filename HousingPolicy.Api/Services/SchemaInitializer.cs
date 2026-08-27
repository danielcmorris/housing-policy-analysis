using Dapper;
using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Applies schema.sql on startup (every statement is CREATE ... IF NOT EXISTS,
/// so it is idempotent) and ensures the congress_gov provenance row exists.
/// schema.sql is the source of truth — same idiom as the Python init_schema().
/// </summary>
public sealed class SchemaInitializer
{
    private readonly DataLayerBase _dl;
    private readonly CongressOptions _opt;
    private readonly ILogger<SchemaInitializer> _log;

    public SchemaInitializer(DataLayerBase dl, IOptions<CongressOptions> opt, ILogger<SchemaInitializer> log)
    {
        _dl = dl;
        _opt = opt.Value;
        _log = log;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        var ddl = await File.ReadAllTextAsync(schemaPath, ct);

        await using var con = await _dl.OpenConnectionAsync(ct);
        await con.ExecuteAsync(ddl);
        await con.ExecuteAsync(
            """
            INSERT INTO sources (source_id, name, publisher, url)
            VALUES ('congress_gov', 'api.congress.gov v3', 'Library of Congress', @Url)
            ON CONFLICT (source_id) DO UPDATE
              SET name = EXCLUDED.name, publisher = EXCLUDED.publisher, url = EXCLUDED.url
            """,
            new { Url = _opt.BaseUrl });

        _log.LogInformation("Schema applied and congress_gov source row ensured.");
    }
}
