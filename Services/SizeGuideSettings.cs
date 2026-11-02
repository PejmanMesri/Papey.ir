using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

public sealed class SizeGuideSettings(IDbContextFactory<AppDbContext> factory, ProductDraftUploads uploads)
{
    public async Task<bool> CanManageAsync(ClaimsPrincipal? principal)
    {
        if (principal is null || !principal.IsInRole("Owner")) return false;
        await using var db = await factory.CreateDbContextAsync();
        var user = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id) : null;
        return user?.IsAdmin == true && AccountAccess.Valid(principal, user, roles: true);
    }
    public async Task<string> ReadAsync(int? id, string? name)
    {
        await using var db = await factory.CreateDbContextAsync();
        var type = id is int value ? await db.ProductTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == value)
            : await db.ProductTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name);
        return ProductSizeGuides.Resolve(type?.SizeGuideImageUrl, type?.Name ?? name);
    }
    public async Task<bool> SaveAsync(ClaimsPrincipal? principal, int id, ProductDraftUploads.Upload upload)
    {
        if (!await CanManageAsync(principal) || upload.IsVideo) return false;
        await using var db = await factory.CreateDbContextAsync();
        var type = await db.ProductTypes.SingleOrDefaultAsync(t => t.Id == id);
        if (type is null) return false;
        var path = uploads.CopyToPermanent(upload, false);
        try
        {
            type.SizeGuideImageUrl = "/uploads/images/" + Path.GetFileName(path);
            await db.SaveChangesAsync(); uploads.Delete(upload); return true;
        }
        catch { File.Delete(path); throw; }
    }
}
