using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Services;

namespace Papey.Endpoints;

public static class PrintingEndpoints
{
    public sealed record LoginRequest(string Identifier, string Password);
    public sealed record ClaimRequest(string ClientId);
    public sealed record LeaseRequest(string ClientId, Guid LeaseToken, string? Error = null);

    public static void MapPrintingEndpoints(this WebApplication app)
    {
        const string prefix = "/api/admin/printing";
        app.MapPost($"{prefix}/login", async (LoginRequest request, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 100 ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.Unauthorized();
            var user = await auth.ValidateLoginAsync(request.Identifier, request.Password);
            if (user is null || !user.CanManageStore) return Results.Unauthorized();
            await auth.SignInAsync(user);
            return Results.NoContent();
        }).RequireRateLimiting("printing-login");

        var api = app.MapGroup(prefix).RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<PrintingSecurityFilter>();

        api.MapGet("/session", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new
            {
                userId = UserId(http),
                csrfToken = antiforgery.GetAndStoreTokens(http).RequestToken,
                csrfHeader = "X-CSRF-TOKEN",
                hubUrl = "/hubs/admin/printing",
                notification = "PrintJobsAvailable",
                leaseSeconds = (int)OrderPrintQueue.LeaseDuration.TotalSeconds
            }));

        api.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        api.MapGet("/jobs/{id:guid}", async (Guid id, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var job = await db.OrderPrintJobs.AsNoTracking().Where(j => j.Id == id)
                .Select(j => new
                {
                    jobId = j.Id, j.OrderId, j.Order.OrderNumber, j.CreatedAt, j.AvailableAt,
                    j.PrintedAt, j.LeaseExpiresAt, j.Attempts, j.ClientId, j.LastError
                }).SingleOrDefaultAsync(ct);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        api.MapPost("/jobs/claim", async (ClaimRequest request, HttpContext http, OrderPrintQueue queue, CancellationToken ct) =>
        {
            if (!ValidClientId(request.ClientId)) return InvalidClient();
            var job = await queue.ClaimAsync(UserId(http), request.ClientId, ct);
            return job is null ? Results.NoContent() : Results.Ok(job);
        });

        api.MapPost("/jobs/{id:guid}/renew", async (Guid id, LeaseRequest request, HttpContext http, OrderPrintQueue queue, CancellationToken ct) =>
        {
            if (!ValidLease(request)) return InvalidClient();
            var expires = await queue.RenewAsync(id, UserId(http), request.ClientId, request.LeaseToken, ct);
            return expires is null ? LeaseLost() : Results.Ok(new { leaseExpiresAt = expires });
        });

        api.MapPost("/jobs/{id:guid}/complete", async (Guid id, LeaseRequest request, HttpContext http, OrderPrintQueue queue, CancellationToken ct) =>
        {
            if (!ValidLease(request)) return InvalidClient();
            return await queue.CompleteAsync(id, UserId(http), request.ClientId, request.LeaseToken, ct)
                ? Results.NoContent() : LeaseLost();
        });

        api.MapPost("/jobs/{id:guid}/release", async (Guid id, LeaseRequest request, HttpContext http, OrderPrintQueue queue, CancellationToken ct) =>
        {
            if (!ValidLease(request) || request.Error?.Length > 1000) return InvalidClient();
            return await queue.ReleaseAsync(id, UserId(http), request.ClientId, request.LeaseToken, request.Error, ct)
                ? Results.NoContent() : LeaseLost();
        });
    }

    private static int UserId(HttpContext http) => int.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static bool ValidClientId(string? clientId) => !string.IsNullOrWhiteSpace(clientId) && clientId.Length <= 100;
    private static bool ValidLease(LeaseRequest request) => ValidClientId(request.ClientId) && request.LeaseToken != Guid.Empty;
    private static IResult InvalidClient() => Results.BadRequest(new { error = "Invalid clientId, leaseToken, or error length." });
    private static IResult LeaseLost() => Results.Conflict(new { error = "Lease lost or job unavailable. Do not print; claim again." });
}

public sealed class PrintingSecurityFilter(IAntiforgery antiforgery, IDbContextFactory<AppDbContext> factory) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
        if (!int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await db.Users.AnyAsync(u => u.Id == userId && !u.IsDisabled && (u.IsAdmin || u.IsStaff), http.RequestAborted))
            return Results.Forbid();

        if (HttpMethods.IsPost(http.Request.Method))
        {
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "Missing or invalid X-CSRF-TOKEN. Refresh /session." });
            }
        }
        return await next(context);
    }
}
