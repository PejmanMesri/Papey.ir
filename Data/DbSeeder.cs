using Microsoft.EntityFrameworkCore;
using Papey.Models;

namespace Papey.Data;

public static class DbSeeder
{
    private static readonly (string Name, string Hex)[] Colors =
    {
        ("مشکی", "#212529"), ("سفید", "#f8f9fa"), ("سرمه‌ای", "#1d3557"),
        ("قرمز", "#c1121f"), ("طوسی", "#6c757d"), ("کرم", "#e9d8a6")
    };
    private static readonly string[] Sizes = { "S", "M", "L", "XL", "XXL" };
    private static readonly (string Name, string FilterIcon)[] ProductTypes =
    {
        ("تیشرت", "👕"),
        ("هودی", "🧥")
    };

    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        await db.Database.MigrateAsync();

        // ---- Admin user ----
        var adminUsername = config["ADMIN_USERNAME"] ?? "Admin";
        var adminPassword = config["ADMIN_PASSWORD"] ?? "@Admin1234";
        var adminEmail = config["ADMIN_EMAIL"] ?? "admin@papey.ir";

        if (!await db.Users.AnyAsync(u => u.Username == adminUsername))
        {
            db.Users.Add(new AppUser
            {
                Username = adminUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                Email = adminEmail,
                FullName = "مدیر فروشگاه",
                IsAdmin = true
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Ensures lookup values and links legacy variants to them.</summary>
    private static async Task EnsureColorsSizesAsync(AppDbContext db)
    {
        var existingColors = await db.Colors.Select(c => c.Name).ToListAsync();
        var missingColors = Colors
            .Where(c => !existingColors.Contains(c.Name))
            .Select((c, i) => new Color { Name = c.Name, Hex = c.Hex, DisplayOrder = i })
            .ToList();
        if (missingColors.Count > 0)
            db.Colors.AddRange(missingColors);

        var existingSizes = await db.Sizes.Select(s => s.Name).ToListAsync();
        var missingSizes = Sizes
            .Where(s => !existingSizes.Contains(s))
            .Select((s, i) => new Size { Name = s, DisplayOrder = i })
            .ToList();
        if (missingSizes.Count > 0)
            db.Sizes.AddRange(missingSizes);

        if (missingColors.Count > 0 || missingSizes.Count > 0)
            await db.SaveChangesAsync();

        var colors = await db.Colors.ToListAsync();
        var sizes = await db.Sizes.ToListAsync();
        foreach (var v in await db.ProductVariants.ToListAsync())
        {
            v.ColorId = colors.FirstOrDefault(c => c.Name == v.Color)?.Id;
            v.SizeId = sizes.FirstOrDefault(s => s.Name == v.Size)?.Id;
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureProductTypesAsync(AppDbContext db)
    {
        var existing = await db.ProductTypes.ToListAsync();
        var missing = ProductTypes
            .Where(seed => existing.All(type => type.Name != seed.Name))
            .Select((seed, index) => new ProductTypeEntity { Name = seed.Name, FilterIcon = seed.FilterIcon, DisplayOrder = index })
            .ToList();
        var changed = false;
        foreach (var seed in ProductTypes)
        {
            var type = existing.FirstOrDefault(item => item.Name == seed.Name);
            if (type is not null && string.IsNullOrWhiteSpace(type.FilterIcon))
            {
                type.FilterIcon = seed.FilterIcon;
                changed = true;
            }
        }

        if (missing.Count > 0) db.ProductTypes.AddRange(missing);
        if (missing.Count > 0 || changed) await db.SaveChangesAsync();
    }

    /// <summary>Initialize lookup values and categories without creating demo products.</summary>
    public static async Task EnsureStoreSeededAsync(AppDbContext db)
    {
        await EnsureColorsSizesAsync(db);
        await EnsureProductTypesAsync(db);
        if (!await db.Categories.AnyAsync()) await SeedStoreAsync(db);
    }

    private static async Task SeedStoreAsync(AppDbContext db)
    {
        var subcategoryDefinitions = new[]
        {
            ("انیمه", "anime"), ("تاریخی", "historical"), ("گیم", "gaming"),
            ("هنری", "art"), ("موسیقی", "music"), ("ماشین", "cars"), ("سایر", "other")
        };
        var typeByName = (await db.ProductTypes.OrderBy(type => type.DisplayOrder).ToListAsync())
            .ToDictionary(type => type.Name);
        var categories = new List<Category>();
        foreach (var type in typeByName.Values)
        {
            var slugPrefix = type.Name == "تیشرت" ? "tshirts" : "hoodies";
            categories.AddRange(subcategoryDefinitions.Select((item, index) => new Category
            {
                Name = item.Item1,
                Slug = $"{slugPrefix}-{item.Item2}",
                Description = $"{item.Item1} {type.Name}",
                ProductTypeId = type.Id,
                DisplayOrder = index,
                ShowInNav = true
            }));
        }
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();

    }
}
