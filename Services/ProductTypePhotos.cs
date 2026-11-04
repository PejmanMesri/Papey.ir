using Microsoft.EntityFrameworkCore;
using Papey.Data;
namespace Papey.Services;

public sealed class ProductTypePhotos(IDbContextFactory<AppDbContext> factory)
{
    private Task<Dictionary<int, string>>? _photos;
    public async Task<string?> ReadAsync(int id) => (await (_photos ??= LoadAsync())).GetValueOrDefault(id);
    private async Task<Dictionary<int, string>> LoadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Products.AsNoTracking().Where(p => p.IsActive && p.ImageUrl != null && p.ImageUrl != "")
            .OrderByDescending(p => p.HomeHeroSlot != null).ThenByDescending(p => p.CreatedAt)
            .Select(p => new { Type = p.ProductTypeId ?? p.Category!.ProductTypeId, p.ImageUrl }).ToListAsync();
        return rows.Where(p => p.Type != null).GroupBy(p => p.Type!.Value).ToDictionary(g => g.Key, g => g.First().ImageUrl!);
    }
}
