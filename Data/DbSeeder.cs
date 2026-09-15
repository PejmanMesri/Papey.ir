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
    private static readonly string[] ProductTypeNames = { "تیشرت", "هودی" };

    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        await db.Database.MigrateAsync();

        // ---- Product types (تیشرت, هودی) — always ensured on first run ----
        if (!await db.ProductTypes.AnyAsync())
        {
            db.ProductTypes.AddRange(ProductTypeNames.Select((n, i) => new ProductTypeEntity { Name = n, DisplayOrder = i }));
            await db.SaveChangesAsync();
        }

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

        if (await db.Categories.AnyAsync()) return; // already seeded
    }

    /// <summary>Runs even when categories exist: backfills Color/Size tables, variant links, and media.</summary>
    public static async Task EnsureColorsSizesAsync(AppDbContext db)
    {
        if (await db.Colors.AnyAsync()) return;

        var colorEntities = Colors.Select((c, i) => new Color { Name = c.Name, Hex = c.Hex, DisplayOrder = i }).ToList();
        db.Colors.AddRange(colorEntities);
        var sizeEntities = Sizes.Select((s, i) => new Size { Name = s, DisplayOrder = i }).ToList();
        db.Sizes.AddRange(sizeEntities);
        await db.SaveChangesAsync();

        var colors = await db.Colors.ToListAsync();
        var sizes = await db.Sizes.ToListAsync();
        foreach (var v in await db.ProductVariants.ToListAsync())
        {
            v.ColorId = colors.FirstOrDefault(c => c.Name == v.Color)?.Id;
            v.SizeId = sizes.FirstOrDefault(s => s.Name == v.Size)?.Id;
        }
        await db.SaveChangesAsync();

        if (!await db.ProductMedia.AnyAsync())
        {
            foreach (var p in await db.Products.ToListAsync())
                if (!string.IsNullOrEmpty(p.ImageUrl))
                    db.ProductMedia.Add(new ProductMedia { ProductId = p.Id, Url = p.ImageUrl, Type = MediaType.Image, Alt = p.Name });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Seeds the full store (categories/products/variants) if the Categories table is empty.</summary>
    public static async Task EnsureStoreSeededAsync(AppDbContext db)
    {
        if (await db.Categories.AnyAsync()) return;
        await SeedStoreAsync(db);
    }

    private static async Task SeedStoreAsync(AppDbContext db)
    {

        // ---- Colors & Sizes (lookup tables, editable in admin) ----
        var colorEntities = Colors.Select((c, i) => new Color { Name = c.Name, Hex = c.Hex, DisplayOrder = i }).ToList();
        db.Colors.AddRange(colorEntities);
        var sizeEntities = Sizes.Select((s, i) => new Size { Name = s, DisplayOrder = i }).ToList();
        db.Sizes.AddRange(sizeEntities);
        await db.SaveChangesAsync();

        // ---- Categories (mirroring tshock.ir) ----
        var cats = new[]
        {
            new Category { Name = "ایرانی", Slug = "irani", Description = "طرح‌های اصیل ایرانی با الهام از فرهنگ و تاریخ ایران", DisplayOrder = 1 },
            new Category { Name = "پینترستی", Slug = "pinteresti", Description = "طرح‌های ترند و مینیمال پینترستی", DisplayOrder = 2 },
            new Category { Name = "ماشینی", Slug = "mashini", Description = "طرح‌های مخصوص علاقه‌مندان به خودرو", DisplayOrder = 3 },
            new Category { Name = "ورزشی", Slug = "varzeshi", Description = "تیشرت‌های ورزشی و باشگاهی", DisplayOrder = 4 },
            new Category { Name = "ست", Slug = "set", Description = "ست‌های زوجی و خانوادگی", DisplayOrder = 5 },
            new Category { Name = "کاستوم", Slug = "custom", Description = "طرح دلخواه خودت را روی تیشرت داشته باش", DisplayOrder = 6 },
        };
        db.Categories.AddRange(cats);
        await db.SaveChangesAsync();

        // ---- Products ----
        var rnd = new Random(42);
        var products = new[]
        {
            new Product { Name = "تیشرت دماوند", Slug = "tshirt-damavand", CategoryId = cats[0].Id, Price = 1650000, OldPrice = 1950000, ImageUrl = "/img/products/damavand.svg", IsFeatured = true, IsNew = true,
                Description = "تیشرت نخ‌پنبه ۱۰۰٪ با طرح قله دماوند. چاپ دیجیتال با دوام بالا، مناسب استفاده روزمره." },
            new Product { Name = "تیشرت پارسه", Slug = "tshirt-persepolis", CategoryId = cats[0].Id, Price = 1650000, ImageUrl = "/img/products/persepolis.svg", IsFeatured = true, IsNew = true,
                Description = "طرح تخت جمشید با ترکیب رنگ خاص. پارچه سوپر پنبه، تنفس‌پذیر و لطیف." },
            new Product { Name = "تیشرت Persian Legacy", Slug = "tshirt-persian-legacy", CategoryId = cats[0].Id, Price = 1650000, ImageUrl = "/img/products/persian-legacy.svg", IsFeatured = true,
                Description = "افتخار به میراث ایرانی با تایپوگرافی خاص. مناسب استایل خیابانی." },
            new Product { Name = "تیشرت ورسک", Slug = "tshirt-versk", CategoryId = cats[0].Id, Price = 1650000, ImageUrl = "/img/products/versk.svg", IsFeatured = true,
                Description = "پل ورسک، نماد مهندسی ایران. چاپ سیلک دستی با کیفیت عالی." },
            new Product { Name = "تیشرت البرز", Slug = "tshirt-alborz", CategoryId = cats[0].Id, Price = 1450000, ImageUrl = "/img/products/alborz.svg", IsNew = true,
                Description = "رشته‌کوه البرز با طراحی مینیمال. پارچه نرم و بادوام." },
            new Product { Name = "تیشرت دریای خزر", Slug = "tshirt-caspian", CategoryId = cats[0].Id, Price = 1450000, ImageUrl = "/img/products/caspians.svg",
                Description = "موج‌های خزر با رنگ‌بندی آرامش‌بخش. مناسب سفر و مهمانی." },
            new Product { Name = "تیشرت مینیمال پینترستی", Slug = "tshirt-pinterest-minimal", CategoryId = cats[1].Id, Price = 1250000, ImageUrl = "/img/products/pinterest-minimal.svg", IsFeatured = true, IsNew = true,
                Description = "طرح مینیمال و شیک با رنگ‌های پاستلی. ترند امسال." },
            new Product { Name = "تیشرت وینتج", Slug = "tshirt-pinterest-vintage", CategoryId = cats[1].Id, Price = 1350000, OldPrice = 1550000, ImageUrl = "/img/products/pinterest-vintage.svg", IsFeatured = true,
                Description = "استایل وینتج با افکت کهنه‌نگاری. پارچه اسلپ باکیفیت." },
            new Product { Name = "تیشرت خودرو کلاسیک", Slug = "tshirt-car-classic", CategoryId = cats[2].Id, Price = 1550000, ImageUrl = "/img/products/car-classic.svg", IsFeatured = true,
                Description = "طرح پیکان کلاسیک برای عاشقان خودروهای قدیمی." },
            new Product { Name = "تیشرت اسپرت", Slug = "tshirt-car-sport", CategoryId = cats[2].Id, Price = 1550000, ImageUrl = "/img/products/car-sport.svg", IsNew = true,
                Description = "سوپراسپرت قرمز با خطوط دینامیک. چاپ براق و بادوام." },
            new Product { Name = "تیشرت فوتبال", Slug = "tshirt-football", CategoryId = cats[3].Id, Price = 1150000, ImageUrl = "/img/products/sport-football.svg", IsFeatured = true,
                Description = "برای هواداران واقعی فوتبال. پارچه خنک و راحت." },
            new Product { Name = "تیشرت بدنسازی", Slug = "tshirt-gym", CategoryId = cats[3].Id, Price = 1150000, ImageUrl = "/img/products/sport-gym.svg",
                Description = "انگیزشی برای تمرین‌های سنگین. الیاف ضدتعریق." },
            new Product { Name = "ست زوجی", Slug = "set-couple", CategoryId = cats[4].Id, Price = 2900000, OldPrice = 3300000, ImageUrl = "/img/products/set-couple.svg", IsFeatured = true,
                Description = "ست دو عددی زوجی با طرح مکمل. بهترین هدیه." },
            new Product { Name = "ست خانوادگی", Slug = "set-family", CategoryId = cats[4].Id, Price = 4200000, ImageUrl = "/img/products/set-family.svg",
                Description = "ست چهار عددی خانوادگی با رنگ‌بندی هماهنگ." },
        };
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        // ---- Variants: every product × all colors × all sizes ----
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
                        Stock = rnd.Next(0, 15)
                    });
        db.ProductVariants.AddRange(variants);

        // ---- Media gallery: seed each product with its main image ----
        foreach (var p in products)
            db.ProductMedia.Add(new ProductMedia { ProductId = p.Id, Url = p.ImageUrl!, Type = MediaType.Image, Alt = p.Name, DisplayOrder = 0 });

        await db.SaveChangesAsync();
    }
}
