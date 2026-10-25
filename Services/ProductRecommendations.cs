using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed record RelatedSelection(List<Product> SameDesign, List<Product> Similar);

public sealed class ProductRecommendations(IDbContextFactory<AppDbContext> factory)
{
    public static string NormalizeDesign(string text, IEnumerable<string> typeNames)
    {
        text = text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').ToLowerInvariant();
        foreach (var type in typeNames.Concat(new[] { "تیشرت", "تی شرت", "هودی", "لانگ اسلیو", "long sleeve", "hoodie", "t-shirt" }).OrderByDescending(t => t.Length))
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(type.ToLowerInvariant().Replace('\u200c', ' ')) + @"(?![\p{L}\p{N}])", " ");
        return Regex.Replace(text, @"[\s\p{P}]+", " ").Trim();
    }
    public async Task<RelatedSelection> ReadAsync(int productId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Include(p => p.Category).Include(p => p.ProductType).ToListAsync();
        var current = products.FirstOrDefault(p => p.Id == productId);
        if (current is null) return new(new(), new());
        var types = await db.ProductTypes.AsNoTracking().Select(t => t.Name).ToListAsync();
        var normalized = NormalizeDesign(current.Name, types);
        bool SameDesign(Product other) => current.ProductTypeId != other.ProductTypeId && (
            !string.IsNullOrWhiteSpace(current.DesignCode)
                ? string.Equals(current.DesignCode.Trim(), other.DesignCode?.Trim(), StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrWhiteSpace(other.DesignCode) && normalized.Length > 0 && NormalizeDesign(other.Name, types) == normalized);
        var candidates = products.Where(p => p.Id != current.Id).ToList();
        var same = candidates.Where(SameDesign).OrderBy(p => p.ProductType?.DisplayOrder).ThenBy(p => p.Id).Take(4).ToList();
        var selected = same.Select(p => p.Id).ToHashSet();
        int Score(Product p) => (p.CategoryId == current.CategoryId ? 6 : p.Category.Name == current.Category.Name ? 4 : 0)
            + (p.ProductTypeId == current.ProductTypeId ? 2 : 0);
        var similar = candidates.Where(p => !selected.Contains(p.Id) && Score(p) > 0).OrderByDescending(Score)
            .ThenByDescending(p => p.IsFeatured).ThenByDescending(p => p.CreatedAt).Take(4).ToList();
        return new(same, similar);
    }
}
