using Microsoft.EntityFrameworkCore;
using Papey.Data;
namespace Papey.Services;

public sealed class ProductTypePhotos(IDbContextFactory<AppDbContext> factory, HomepageSettings homepage)
{
    private Task<Dictionary<int, string>>? _photos;
    private long _revision = -1;
    private DateTime _loaded;
    public async Task<string?> ReadAsync(int id)
    {
        if (_photos is null || _revision != homepage.Revision || DateTime.UtcNow - _loaded > TimeSpan.FromSeconds(10))
        { _revision = homepage.Revision; _loaded = DateTime.UtcNow; _photos = LoadAsync(); }
        return (await _photos).GetValueOrDefault(id);
    }
    private async Task<Dictionary<int, string>> LoadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Products.AsNoTracking().Where(p => p.IsActive && p.ImageUrl != null && p.ImageUrl != "")
            .OrderByDescending(p => p.HomeHeroSlot != null).ThenByDescending(p => p.CreatedAt)
            .Select(p => new { Type = p.ProductTypeId ?? p.Category!.ProductTypeId, p.ImageUrl }).ToListAsync();
        var result = rows.Where(p => p.Type != null).GroupBy(p => p.Type!.Value).ToDictionary(g => g.Key, g => g.First().ImageUrl!);
        var saved = await homepage.ReadAsync();
        foreach (var type in await db.ProductTypes.AsNoTracking().ToListAsync())
        {
            var photo = saved.TypePhotos.GetValueOrDefault(type.Id) ?? HomepageSettings.DefaultPhoto(type.Name);
            if (photo is not null) result[type.Id] = photo;
        }
        return result;
    }
}
