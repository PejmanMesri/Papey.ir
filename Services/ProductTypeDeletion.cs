using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

public enum ProductTypeDeleteResult { Deleted, HasProducts, NotFound }

public static class ProductTypeDeletion
{
    public static async Task<ProductTypeDeleteResult> DeleteIfEmptyAsync(AppDbContext db, int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Block new product/category references while checking and removing the type.
        var type = await db.ProductTypes.FromSqlInterpolated($"SELECT * FROM \"ProductTypes\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (type is null) return ProductTypeDeleteResult.NotFound;
        if (await db.Products.AnyAsync(p => p.ProductTypeId == id)) return ProductTypeDeleteResult.HasProducts;

        // Categories and their product links survive; only the optional type is removed.
        await db.Categories.Where(c => c.ProductTypeId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ProductTypeId, (int?)null));
        db.ProductTypes.Remove(type);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ProductTypeDeleteResult.Deleted;
    }
}
