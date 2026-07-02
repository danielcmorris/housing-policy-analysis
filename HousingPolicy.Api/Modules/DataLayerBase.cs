using System.Data;
using Dapper;
using Npgsql;

namespace HousingPolicy.Api.Modules;

/// <summary>
/// Thin Npgsql + Dapper data-access base, matching the house pattern
/// (DCElectricWebAPI.Modules.DataLayerBase): a scoped service that hands out
/// freshly-opened pooled connections and wraps the common Dapper calls.
/// Raw SQL against schema.sql is deliberate — same "schema is the source of
/// truth, no ORM" stance as the rest of the corpus.
/// </summary>
public class DataLayerBase
{
    public readonly string ConnectionString;

    public DataLayerBase(IConfiguration configuration)
    {
        ConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>A NEW, OPENED connection. Relies on Npgsql connection pooling (the Web-API standard).</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var db = await OpenConnectionAsync();
        return await db.QueryAsync<T>(sql, parameters);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        await using var db = await OpenConnectionAsync();
        return await db.QuerySingleOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var db = await OpenConnectionAsync();
        return await db.ExecuteAsync(sql, parameters);
    }
}
