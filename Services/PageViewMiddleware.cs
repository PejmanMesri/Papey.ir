using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

/// <summary>Records one PageView row per storefront request (skips admin, framework and static files).</summary>
public class PageViewMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string[] SkipPrefixes =
        { "/admin", "/_framework", "/_blazor", "/_content", "/img", "/uploads", "/css", "/js", "/favicon", "/account", "/healthz" };

    public PageViewMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IDbContextFactory<AppDbContext> dbFactory)
    {
        await _next(ctx);

        if (ctx.Request.Method != "GET") return;
        var path = ctx.Request.Path.Value ?? "";
        if (path.Contains('.') || SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return;
        if (ctx.Response.StatusCode != 200) return;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.PageViews.Add(new PageView { Path = path });
            await db.SaveChangesAsync();
        }
        catch { /* never break the request */ }
    }
}
