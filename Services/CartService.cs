using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

/// <summary>
/// Basket persisted in distributed cache (in-memory locally, Redis in containers).
/// Shared across Blazor circuits so a full navigation does not lose items.
/// </summary>
public class CartService
{
    private const string CartKey = "papey:cart";
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDistributedCache _cache;

    public List<CartItem> Items { get; private set; } = new();
    public event Action? OnChange;

    public CartService(IDbContextFactory<AppDbContext> dbFactory, IDistributedCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    public int TotalQuantity => Items.Sum(i => i.Quantity);
    public decimal Total => Items.Sum(i => i.LineTotal);

    public async Task LoadAsync()
    {
        try
        {
            var json = await _cache.GetStringAsync(CartKey);
            if (!string.IsNullOrEmpty(json))
                Items = JsonSerializer.Deserialize<List<CartItem>>(json) ?? new();
        }
        catch { /* keep current */ }
    }

    private async Task PersistAsync()
    {
        try
        {
            await _cache.SetStringAsync(CartKey, JsonSerializer.Serialize(Items),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(7) });
        }
        catch { /* keep in-memory only */ }
        OnChange?.Invoke();
    }

    public async Task<bool> AddAsync(int productId, string color, string size, int quantity = 1)
    {
        await LoadAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var variant = await db.ProductVariants.Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Color == color && v.Size == size);
        if (variant is null || quantity <= 0 || !ProductInventory.CanOrder(variant, quantity)) return false;

        var existing = Items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
        if (existing is not null)
        {
            if ((long)existing.Quantity + quantity > ProductInventory.MaximumQuantity(variant)) return false;
            existing.MaxStock = ProductInventory.MaximumQuantity(variant);
            existing.Quantity += quantity;
        }
        else
        {
            Items.Add(new CartItem
            {
                ProductId = productId,
                ProductVariantId = variant.Id,
                ProductName = variant.Product.Name,
                ImageUrl = variant.Product.ImageUrl,
                Color = variant.Color,
                ColorHex = variant.ColorHex,
                Size = variant.Size,
                UnitPrice = variant.EffectivePrice,
                Quantity = quantity,
                MaxStock = ProductInventory.MaximumQuantity(variant)
            });
        }
        await PersistAsync();
        return true;
    }

    public async Task RemoveAsync(int variantId)
    {
        await LoadAsync();
        Items.RemoveAll(i => i.ProductVariantId == variantId);
        await PersistAsync();
    }

    public async Task SetQuantityAsync(int variantId, int qty)
    {
        await LoadAsync();
        var item = Items.FirstOrDefault(i => i.ProductVariantId == variantId);
        if (item is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var variant = await db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId);
        if (variant is null || !ProductInventory.CanOrder(variant, 1))
        {
            Items.Remove(item);
        }
        else
        {
            item.MaxStock = ProductInventory.MaximumQuantity(variant);
            item.Quantity = Math.Clamp(qty, 1, item.MaxStock);
        }
        await PersistAsync();
    }

    public async Task ClearAsync()
    {
        Items.Clear();
        await PersistAsync();
    }

    public void Remove(int variantId) => _ = RemoveAsync(variantId);
    public void SetQuantity(int variantId, int qty) => _ = SetQuantityAsync(variantId, qty);
    public void Clear() => _ = ClearAsync();
}
