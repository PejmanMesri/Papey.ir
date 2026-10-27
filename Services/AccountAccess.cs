using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public static class AccountAccess
{
    public const string VersionClaim = "papey:auth-version";
    public static ClaimsPrincipal Principal(AppUser user)
    {
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username),
            new(VersionClaim, user.AuthVersion.ToString()), new(ClaimTypes.Role, user.CanManageStore ? "Admin" : "User")
        };
        if (user.IsAdmin && !user.IsDisabled) claims.Add(new(ClaimTypes.Role, "Owner"));
        else if (user.IsStaff && !user.IsDisabled) claims.Add(new(ClaimTypes.Role, "Staff"));
        return new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
    public static bool Valid(ClaimsPrincipal principal, AppUser? user, bool roles = false)
    {
        var raw = principal.FindFirstValue(VersionClaim);
        var version = raw is null ? 0 : int.TryParse(raw, out var parsed) ? parsed : -1;
        return user is not null && !user.IsDisabled && principal.Identity?.IsAuthenticated == true
            && principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() && version == user.AuthVersion
            && (!roles || (principal.IsInRole("Admin") == user.CanManageStore && principal.IsInRole("Owner") == user.IsAdmin));
    }
    public static async Task ValidateCookie(CookieValidatePrincipalContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var principal = context.Principal!;
        var user = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, context.HttpContext.RequestAborted) : null;
        if (!Valid(principal, user))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        // Upgrade existing owner cookies in place; deployments don't revoke sessions.
        if (principal.FindFirstValue(VersionClaim) is null || !Valid(principal, user, roles: true))
        {
            context.ReplacePrincipal(Principal(user!));
            context.ShouldRenew = true;
        }
    }
}

public sealed class StoreAuthenticationStateProvider(ILoggerFactory logger, IDbContextFactory<AppDbContext> factory)
    : RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(10);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = int.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) : null;
        return AccountAccess.Valid(state.User, user, roles: true);
    }
}

public sealed class AccountCircuitHandler(AuthenticationStateProvider authentication, IDbContextFactory<AppDbContext> factory) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) => async context =>
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            await using var db = await factory.CreateDbContextAsync();
            var user = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) : null;
            if (!AccountAccess.Valid(principal, user, roles: true))
            {
                ((StoreAuthenticationStateProvider)authentication).SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
                return;
            }
        }
        await next(context);
    };
}
