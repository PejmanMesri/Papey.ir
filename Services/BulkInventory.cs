using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public enum BulkStockMode { Unavailable, Limited, Unlimited }

public static class BulkInventory
{
    public static async Task<int> ApplyAsync(AppDbContext db, int typeId, int colorId, int sizeId,
        BulkStockMode mode, int quantity)
    {
        if (!Enum.IsDefined(mode) || (mode == BulkStockMode.Limited && quantity < 1))
            throw new ArgumentException("تعداد موجودی باید حداقل یک باشد.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var color = await db.Colors.SingleAsync(c => c.Id == colorId && c.IsActive);
        var size = await db.Sizes.SingleAsync(s => s.Id == sizeId && s.IsActive);
        if (!await db.ProductTypes.AnyAsync(t => t.Id == typeId && t.IsActive))
            throw new InvalidOperationException("نوع محصول فعال نیست.");

        var products = await db.Products.FromSqlInterpolated($"""
            SELECT p.* FROM "Products" p JOIN "Categories" c ON c."Id" = p."CategoryId"
            WHERE COALESCE(p."ProductTypeId", c."ProductTypeId") = {typeId}
            ORDER BY p."Id" FOR UPDATE OF p
            """).ToListAsync();
        var ids = products.Select(p => p.Id).ToArray();
        var variants = await db.ProductVariants.Where(v => ids.Contains(v.ProductId)
            && (v.ColorId == colorId || (v.ColorId == null && v.Color == color.Name))
            && (v.SizeId == sizeId || (v.SizeId == null && v.Size == size.Name))).ToListAsync();
        var byProduct = variants.ToLookup(v => v.ProductId);

        foreach (var product in products)
        {
            if (!byProduct.Contains(product.Id))
            {
                if (mode == BulkStockMode.Unavailable) continue;
                var variant = new ProductVariant
                {
                    ProductId = product.Id, ColorId = colorId, Color = color.Name, ColorHex = color.Hex,
                    SizeId = sizeId, Size = size.Name
                };
                SetStock(variant, mode, quantity);
                db.ProductVariants.Add(variant);
                continue;
            }

            foreach (var variant in byProduct[product.Id])
                SetStock(variant, mode, quantity);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return mode == BulkStockMode.Unavailable ? byProduct.Count : products.Count;
    }

    private static void SetStock(ProductVariant variant, BulkStockMode mode, int quantity)
    {
        variant.IsEnabled = mode != BulkStockMode.Unavailable;
        if (mode == BulkStockMode.Unavailable) return;
        variant.UnlimitedStockOverride = mode == BulkStockMode.Unlimited;
        if (mode == BulkStockMode.Limited) variant.Stock = quantity;
    }
}
