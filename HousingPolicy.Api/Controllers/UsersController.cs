using System.Security.Claims;
using HousingPolicy.Api.Options;
using HousingPolicy.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HousingPolicy.Api.Controllers;

/// <summary>
/// The user-manager module. /api/auth/config tells the SPA whether auth is
/// enforced; /api/users/me upserts + returns the caller's local record; the
/// /api/admin/users endpoints (list, role, disable) sit behind the global
/// /api/admin gate in Program.cs like every other admin surface.
/// </summary>
[ApiController]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _users;
    private readonly AuthOptions _opt;

    public UsersController(UserService users, IOptions<AuthOptions> options)
    {
        _users = users;
        _opt = options.Value;
    }

    /// <summary>Public auth bootstrap for the SPA (all values are public).</summary>
    [HttpGet("api/auth/config")]
    public IActionResult Config() => Ok(new
    {
        enabled = _opt.Enabled,
        domain = _opt.Domain,
        client_id = _opt.ClientId,
        audience = _opt.Audience,
    });

    /// <summary>
    /// The caller's own record, upserted from the validated token. 401 when
    /// auth is enabled and no valid token came along; when auth is disabled
    /// there is no caller identity, so a placeholder admin is returned to keep
    /// the SPA working exactly as before the module existed.
    /// </summary>
    [HttpGet("api/users/me")]
    public async Task<IActionResult> Me()
    {
        if (!_opt.Enabled)
            return Ok(new { sub = "", email = (string?)null, name = "Local admin (auth disabled)",
                            picture = (string?)null, role = "admin", disabled = false });
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub))
            return Unauthorized(new { detail = "no valid token" });

        // Standard OIDC profile claims when present in the access token; the
        // SPA also passes them explicitly on first login via POST below.
        var row = await _users.UpsertLoginAsync(sub,
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
            User.FindFirstValue("name"), User.FindFirstValue("picture"));
        return row.Disabled
            ? StatusCode(StatusCodes.Status403Forbidden, new { detail = "this account is disabled" })
            : Ok(ToDto(row));
    }

    public sealed record ProfileForm(string? Email, string? Name, string? Picture);

    /// <summary>
    /// Same as GET /me but carries the OIDC profile from the SPA's id token —
    /// access tokens often omit email/name, so the client reports them once
    /// after login and the row fills in.
    /// </summary>
    [HttpPost("api/users/me")]
    public async Task<IActionResult> UpdateMe([FromBody] ProfileForm form)
    {
        if (!_opt.Enabled) return Ok(new { ok = true });
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub))
            return Unauthorized(new { detail = "no valid token" });
        var row = await _users.UpsertLoginAsync(sub, form.Email, form.Name, form.Picture);
        return row.Disabled
            ? StatusCode(StatusCodes.Status403Forbidden, new { detail = "this account is disabled" })
            : Ok(ToDto(row));
    }

    [HttpGet("api/admin/users")]
    public async Task<IActionResult> List(string? q = null) =>
        Ok((await _users.ListAsync(q)).Select(ToDto));

    public sealed record RoleForm(string Role);
    public sealed record DisabledForm(bool Disabled);

    [HttpPost("api/admin/users/{userId:long}/role")]
    public async Task<IActionResult> SetRole(long userId, [FromBody] RoleForm form)
    {
        if (!UserService.Roles.Contains(form.Role))
            return BadRequest(new { detail = $"role must be one of: {string.Join(", ", UserService.Roles)}" });
        if (await _users.SetRoleAsync(userId, form.Role) == 0)
            return NotFound(new { detail = $"no user {userId}" });
        return Ok(new { user_id = userId, role = form.Role });
    }

    [HttpPost("api/admin/users/{userId:long}/disabled")]
    public async Task<IActionResult> SetDisabled(long userId, [FromBody] DisabledForm form)
    {
        if (await _users.SetDisabledAsync(userId, form.Disabled) == 0)
            return NotFound(new { detail = $"no user {userId}" });
        return Ok(new { user_id = userId, disabled = form.Disabled });
    }

    private static object ToDto(UserService.UserRow u) => new
    {
        user_id = u.UserId,
        sub = u.Auth0Sub,
        u.Email,
        u.Name,
        u.Picture,
        u.Role,
        u.Disabled,
        first_login = u.FirstLogin,
        last_login = u.LastLogin,
    };
}
