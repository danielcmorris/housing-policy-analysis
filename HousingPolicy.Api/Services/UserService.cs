using HousingPolicy.Api.Modules;
using HousingPolicy.Api.Options;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Services;

/// <summary>
/// Local user records behind Auth0. Auth0 owns identity; this table owns
/// authorization: one row per Auth0 subject with the role and disabled flag,
/// upserted from the validated token on login.
/// </summary>
public sealed class UserService
{
    private readonly DataLayerBase _dl;
    private readonly AuthOptions _opt;

    public UserService(DataLayerBase dl, IOptions<AuthOptions> options)
    {
        _dl = dl;
        _opt = options.Value;
    }

    public sealed class UserRow
    {
        public long UserId { get; set; }
        public string Auth0Sub { get; set; } = "";
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Picture { get; set; }
        public string Role { get; set; } = "member";
        public bool Disabled { get; set; }
        public DateTime FirstLogin { get; set; }
        public DateTime LastLogin { get; set; }
    }

    public static readonly string[] Roles = { "admin", "member" };

    /// <summary>
    /// Record a login: insert on first sight, refresh profile + last_login
    /// after. The seed-admin email is always promoted to admin, so the first
    /// administrator exists without hand-editing the database.
    /// </summary>
    public async Task<UserRow> UpsertLoginAsync(string sub, string? email, string? name, string? picture)
    {
        var seedAdmin = !string.IsNullOrEmpty(_opt.SeedAdminEmail) &&
                        string.Equals(email, _opt.SeedAdminEmail, StringComparison.OrdinalIgnoreCase);
        return (await _dl.QuerySingleOrDefaultAsync<UserRow>(
            """
            INSERT INTO users (auth0_sub, email, name, picture, role)
            VALUES (@Sub, @Email, @Name, @Picture, CASE WHEN @SeedAdmin THEN 'admin' ELSE 'member' END)
            ON CONFLICT (auth0_sub) DO UPDATE SET
                email = COALESCE(EXCLUDED.email, users.email),
                name = COALESCE(EXCLUDED.name, users.name),
                picture = COALESCE(EXCLUDED.picture, users.picture),
                role = CASE WHEN @SeedAdmin THEN 'admin' ELSE users.role END,
                last_login = now()
            RETURNING user_id, auth0_sub, email, name, picture, role, disabled, first_login, last_login
            """,
            new { Sub = sub, Email = email, Name = name, Picture = picture, SeedAdmin = seedAdmin }))!;
    }

    public Task<UserRow?> GetBySubAsync(string sub) =>
        _dl.QuerySingleOrDefaultAsync<UserRow>(
            """
            SELECT user_id, auth0_sub, email, name, picture, role, disabled, first_login, last_login
            FROM users WHERE auth0_sub = @Sub
            """, new { Sub = sub });

    public async Task<List<UserRow>> ListAsync(string? q)
    {
        var sql = """
            SELECT user_id, auth0_sub, email, name, picture, role, disabled, first_login, last_login
            FROM users WHERE TRUE
            """;
        var p = new Dapper.DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (email ILIKE @Like OR name ILIKE @Like)";
            p.Add("Like", $"%{q.Trim()}%");
        }
        sql += " ORDER BY last_login DESC LIMIT 500";
        return (await _dl.QueryAsync<UserRow>(sql, p)).ToList();
    }

    public Task<int> SetRoleAsync(long userId, string role) =>
        _dl.ExecuteAsync("UPDATE users SET role = @Role WHERE user_id = @Id",
                         new { Id = userId, Role = role });

    public Task<int> SetDisabledAsync(long userId, bool disabled) =>
        _dl.ExecuteAsync("UPDATE users SET disabled = @Disabled WHERE user_id = @Id",
                         new { Id = userId, Disabled = disabled });
}
