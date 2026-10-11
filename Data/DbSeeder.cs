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

    private sealed record SeedProduct(
        string Name,
        string Slug,
        string CategorySlug,
        string ProductTypeName,
        string ImageUrl,
        decimal Price,
        decimal? OldPrice,
        string Description,
        bool IsFeatured,
        bool IsNew);

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

    /// <summary>
    /// Replaces the legacy flat demo catalog once with the hierarchical clothing catalog.
    /// User accounts and saved addresses are intentionally preserved; catalog-bound carts,
    /// favourites, and demo orders are removed because their product references become invalid.
    /// </summary>
    public static async Task EnsureStoreSeededAsync(AppDbContext db)
    {
        var hasCurrentTypeCategories = await db.Categories.AnyAsync()
            && await db.Categories.AllAsync(c => c.ProductTypeId != null);

        if (!hasCurrentTypeCategories && await db.Categories.AnyAsync())
            await ResetCatalogAsync(db);

        await EnsureColorsSizesAsync(db);
        await EnsureProductTypesAsync(db);

        if (await db.Categories.AnyAsync()) return;
        await SeedStoreAsync(db);
    }

    private static async Task ResetCatalogAsync(AppDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.ProductFavorites.ExecuteDeleteAsync();
        await db.OrderItems.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.ProductMedia.ExecuteDeleteAsync();
        await db.ProductDesignMedia.ExecuteDeleteAsync();
        await db.ProductCategories.ExecuteDeleteAsync();
        await db.ProductVariants.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await db.ProductTypes.ExecuteDeleteAsync();
        await db.Colors.ExecuteDeleteAsync();
        await db.Sizes.ExecuteDeleteAsync();

        await transaction.CommitAsync();
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

        var categoryBySlug = categories.ToDictionary(c => c.Slug);
        var seedProducts = new[]
        {
            new SeedProduct("تیشرت انیمه سایه", "tshirt-anime-shadow", "tshirts-anime", "تیشرت", "/img/products/damavand.svg", 1650000, 1950000, "تیشرت نخ‌پنبه با طرح انیمه و چاپ بادوام.", true, true),
            new SeedProduct("تیشرت تاریخ ایران", "tshirt-history-iran", "tshirts-historical", "تیشرت", "/img/products/persepolis.svg", 1650000, null, "طرح الهام‌گرفته از تاریخ و فرهنگ ایران.", true, true),
            new SeedProduct("تیشرت گیمینگ نئون", "tshirt-gaming-neon", "tshirts-gaming", "تیشرت", "/img/products/persian-legacy.svg", 1550000, null, "تیشرت گیمینگ با چاپ دیجیتال پررنگ.", true, false),
            new SeedProduct("تیشرت گالری مدرن", "tshirt-modern-art", "tshirts-art", "تیشرت", "/img/products/versk.svg", 1450000, null, "تیشرت هنری برای استایل روزمره.", false, true),
            new SeedProduct("تیشرت موسیقی خیابانی", "tshirt-street-music", "tshirts-music", "تیشرت", "/img/products/alborz.svg", 1450000, null, "طرح موسیقی با پارچه نرم و تنفس‌پذیر.", false, false),
            new SeedProduct("تیشرت خودرو کلاسیک", "tshirt-classic-car", "tshirts-cars", "تیشرت", "/img/products/car-classic.svg", 1550000, null, "برای عاشقان خودروهای کلاسیک.", true, false),
            new SeedProduct("تیشرت مینیمال", "tshirt-minimal", "tshirts-other", "تیشرت", "/img/products/pinterest-minimal.svg", 1250000, null, "طرح مینیمال مناسب همه استایل‌ها.", false, true),
            new SeedProduct("هودی انیمه قهرمان", "hoodie-anime-hero", "hoodies-anime", "هودی", "/img/products/pinterest-vintage.svg", 2950000, 3250000, "هودی گرم با طرح انیمه و چاپ ماندگار.", true, true),
            new SeedProduct("هودی روایت تاریخ", "hoodie-history-story", "hoodies-historical", "هودی", "/img/products/caspians.svg", 2850000, null, "هودی طرح تاریخی با پارچه دورس باکیفیت.", false, false),
            new SeedProduct("هودی گیمینگ نئون", "hoodie-gaming-neon", "hoodies-gaming", "هودی", "/img/products/sport-football.svg", 3100000, null, "هودی گیمینگ برای روزهای خنک.", true, true),
            new SeedProduct("هودی قلم‌مو", "hoodie-art-brush", "hoodies-art", "هودی", "/img/products/sport-gym.svg", 2900000, null, "هودی هنری با چاپ باکیفیت.", false, false),
            new SeedProduct("هودی ریتم", "hoodie-music-rhythm", "hoodies-music", "هودی", "/img/products/set-couple.svg", 2900000, null, "هودی موسیقی برای استایل خیابانی.", false, true),
            new SeedProduct("هودی ماشین اسپرت", "hoodie-sport-car", "hoodies-cars", "هودی", "/img/products/car-sport.svg", 3150000, null, "هودی ماشین با طراحی پویا.", true, false),
            new SeedProduct("هودی ساده پاپی", "hoodie-papey-basic", "hoodies-other", "هودی", "/img/products/set-family.svg", 2700000, null, "هودی ساده و راحت برای استفاده روزانه.", false, false)
        };

        var products = seedProducts.Select(seed => new Product
        {
            Name = seed.Name,
            Slug = seed.Slug,
            Description = seed.Description,
            Price = seed.Price,
            OldPrice = seed.OldPrice,
            ImageUrl = seed.ImageUrl,
            IsFeatured = seed.IsFeatured,
            IsNew = seed.IsNew,
            CategoryId = categoryBySlug[seed.CategorySlug].Id,
            ProductTypeId = typeByName[seed.ProductTypeName].Id
        }).ToList();
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        db.ProductCategories.AddRange(products.Select((product, index) => new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = categoryBySlug[seedProducts[index].CategorySlug].Id
        }));

        var colorEntities = await db.Colors.OrderBy(c => c.DisplayOrder).ToListAsync();
        var sizeEntities = await db.Sizes.OrderBy(s => s.DisplayOrder).ToListAsync();
        var rnd = new Random(42);
        var variants = new List<ProductVariant>();
        foreach (var p in products)
            foreach (var col in colorEntities)
                foreach (var sz in sizeEntities)
                    variants.Add(new ProductVariant
                    {
                        ProductId = p.Id,
                        Color = col.Name,
                        ColorHex = col.Hex,
                        ColorId = col.Id,
                        Size = sz.Name,
                        SizeId = sz.Id,
                        Stock = rnd.Next(2, 15)
                    });
        db.ProductVariants.AddRange(variants);

        foreach (var p in products)
            db.ProductMedia.Add(new ProductMedia { ProductId = p.Id, Url = p.ImageUrl!, Type = MediaType.Image, Alt = p.Name, DisplayOrder = 0 });

        await db.SaveChangesAsync();
    }
}
