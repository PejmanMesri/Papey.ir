using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

public sealed class TaxSettingsService(IDbContextFactory<AppDbContext> factory)
{
    public event Action? Changed;

    public async Task<bool> ReadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.StoreSettings.Where(s => s.Id == 1).Select(s => s.TaxEnabled).SingleAsync();
    }

    // Keep this row locked until the order snapshot commits, so a toggle cannot
    // race the checkout's accepted quote and stored payment amount.
    public static async Task<bool> ReadForOrderAsync(AppDbContext db)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Order transaction required.");
        var settings = await db.StoreSettings.FromSqlRaw("SELECT * FROM \"StoreSettings\" WHERE \"Id\" = 1 FOR SHARE").AsNoTracking().SingleAsync();
        return settings.TaxEnabled;
    }

    public async Task<int> DiscountAsync() { await using var db=await factory.CreateDbContextAsync(); return await db.StoreSettings.Where(s=>s.Id==1).Select(s=>s.DiscountPercent).SingleAsync(); }
    public async Task UpdateAsync(bool enabled, int discountPercent = 0)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.StoreSettings.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(s => s.TaxEnabled, enabled).SetProperty(s=>s.DiscountPercent,Math.Clamp(discountPercent,0,100)));
        Changed?.Invoke();
    }
}
