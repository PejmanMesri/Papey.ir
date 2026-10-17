using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public static class ProductInventory
{
    public static int MaximumQuantity(ProductVariant variant) =>
        !variant.IsEnabled || !variant.Product.IsActive ? 0 :
        variant.Product.HasUnlimitedStock ? int.MaxValue : Math.Max(0, variant.Stock);

    public static bool CanOrder(ProductVariant variant, int quantity) =>
        quantity > 0 && quantity <= MaximumQuantity(variant);

    // Caller owns the order transaction. Lock both mode and quantity so concurrent
    // checkouts cannot oversell limited stock or race a change of inventory mode.
    public static async Task<ProductVariant?> ReserveAsync(AppDbContext db, int variantId, int quantity, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("An order transaction is required.");
        var rows = await db.ProductVariants.FromSqlInterpolated($"""
            SELECT v.* FROM "ProductVariants" v JOIN "Products" p ON p."Id" = v."ProductId"
            WHERE v."Id" = {variantId} FOR UPDATE OF p, v
            """).ToListAsync(ct);
        var variant = rows.FirstOrDefault();
        if (variant is null) return null;
        await db.Entry(variant).Reference(v => v.Product).LoadAsync(ct);
        if (!CanOrder(variant, quantity)) return null;
        if (!variant.Product.HasUnlimitedStock) variant.Stock -= quantity;
        return variant;
    }
}
