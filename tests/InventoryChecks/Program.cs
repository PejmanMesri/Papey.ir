using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
    Console.WriteLine("PASS: " + description);
}
DotNetEnv.Env.NoClobber().TraversePath().Load();
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? DatabaseConnection.Build(config)) { Pooling = false };
var database = "papey_inventory_checks_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
source.Database = database;
var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
try
{
    await using var db = factory.CreateDbContext();
    await db.Database.MigrateAsync();
    var category = new Category { Name = "Test", Slug = "inventory-check" };
    var unlimited = new Product { Name = "Unlimited", Slug = "unlimited", Category = category, Price = 100 };
    var limited = new Product { Name = "Limited", Slug = "limited", Category = category, Price = 100, HasUnlimitedStock = false };
    var available = new ProductVariant { Product = unlimited, Color = "Black", Size = "M", Stock = 0 };
    var disabled = new ProductVariant { Product = unlimited, Color = "Black", Size = "L", Stock = 20, IsEnabled = false };
    var finite = new ProductVariant { Product = limited, Color = "White", Size = "M", Stock = 3 };
    db.ProductVariants.AddRange(available, disabled, finite);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    Check(await db.Products.AnyAsync(p => p.Id == unlimited.Id && p.HasUnlimitedStock), "New products default to unlimited quantity");
    Check(await db.Products.AnyAsync(p => p.Id == limited.Id && !p.HasUnlimitedStock), "Explicit finite-stock mode persists despite default=true");
    Check(await db.ProductVariants.AnyAsync(v => v.Id == disabled.Id && !v.IsEnabled), "Disabled combinations persist despite default=true");
    Check(ProductInventory.CanOrder(available, 10000) && !ProductInventory.CanOrder(disabled, 1), "Unlimited stock still requires an enabled combination");
    Check(!ProductInventory.CanOrder(finite, 4) && !ProductInventory.CanOrder(available, 0), "Limited stock and positive-quantity validation remain enforced");

    var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    var cart = new CartService(factory, cache);
    Check(await cart.AddAsync(unlimited.Id, "Black", "M", 10000), "Unlimited zero-stock variant can be added in large quantities");
    Check(!await cart.AddAsync(unlimited.Id, "Black", "L", 1), "Disabled variant cannot be added to cart");
    Check(!await cart.AddAsync(limited.Id, "White", "M", 4), "Initial cart add cannot exceed finite stock");
    Check(await cart.AddAsync(limited.Id, "White", "M", 2) && !await cart.AddAsync(limited.Id, "White", "M", 2), "Repeated adds cannot exceed finite stock");
    await cart.SetQuantityAsync(available.Id, 15000);
    Check(cart.Items.Single(i => i.ProductVariantId == available.Id).Quantity == 15000, "Unlimited cart quantities can increase beyond stored stock");
    var privateA = Guid.NewGuid().ToString("N"); var privateB = Guid.NewGuid().ToString("N");
    CartService PrivateCart(string id) { var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); context.Request.Headers.Cookie = CartIdentity.CookieName + "=" + id; return new(factory, cache, new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context }); }
    var cartA = PrivateCart(privateA); var cartB = PrivateCart(privateB);
    Check(await cartA.AddAsync(unlimited.Id, "Black", "M", 2), "Visitor A can create a private basket");
    await cartB.LoadAsync(); Check(cartB.Items.Count == 0, "Visitor B cannot read visitor A basket");
    var sameBrowser = PrivateCart(privateA); await sameBrowser.LoadAsync(); Check(sameBrowser.Items.Single().Quantity == 2, "A private basket survives a fresh browser request");
    await db.Products.Where(p => p.Id == unlimited.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 175));
    await sameBrowser.LoadAsync(); Check(sameBrowser.Items.Single().UnitPrice == 175, "Basket reload uses current database prices before payment");
    await cartB.ClearAsync(); await sameBrowser.LoadAsync(); Check(sameBrowser.Items.Count == 1, "Clearing one visitor basket cannot clear another basket");
    await db.Products.Where(p => p.Id == unlimited.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 100));
    await db.Products.Where(p => p.Id == unlimited.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.HasUnlimitedStock, false));
    await db.ProductVariants.Where(v => v.Id == available.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, 4));
    await cart.SetQuantityAsync(available.Id, 15000);
    Check(cart.Items.Single(i => i.ProductVariantId == available.Id).Quantity == 4, "Cart rechecks a switch from unlimited to finite stock");
    await db.Products.Where(p => p.Id == unlimited.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.HasUnlimitedStock, true));
    await db.ProductVariants.Where(v => v.Id == available.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsEnabled, false));
    await cart.SetQuantityAsync(available.Id, 5);
    Check(cart.Items.All(i => i.ProductVariantId != available.Id), "Disabling a combination prevents stale cart quantity updates");
    await db.ProductVariants.Where(v => v.Id == available.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsEnabled, true));

    async Task<bool> Reserve(int id, int qty, bool commit = true)
    {
        await using var context = factory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var variant = await ProductInventory.ReserveAsync(context, id, qty);
        if (variant is null) return false;
        await context.SaveChangesAsync();
        if (commit) await tx.CommitAsync();
        return true;
    }

    var type = new ProductTypeEntity { Name = "Bulk type" };
    var color = new Color { Name = "Black", Hex = "#000000" };
    var size = new Size { Name = "M" };
    db.AddRange(type, color, size);
    await db.SaveChangesAsync();
    await db.Categories.Where(c => c.Id == category.Id)
        .ExecuteUpdateAsync(set => set.SetProperty(c => c.ProductTypeId, type.Id));

    async Task<int> ApplyBulk(BulkStockMode mode, int quantity = 0)
    {
        await using var context = factory.CreateDbContext();
        return await BulkInventory.ApplyAsync(context, type.Id, color.Id, size.Id, mode, quantity);
    }
    Check(await ApplyBulk(BulkStockMode.Limited, 2) == 2, "Bulk stock updates legacy variants and creates missing combinations");
    Check(!await Reserve(available.Id, 3) && await Reserve(available.Id, 2),
        "Finite override enforces and decrements stock even when the product defaults to unlimited");
    Check(await ApplyBulk(BulkStockMode.Unlimited) == 2, "Bulk stock can make one combination unlimited");
    var added = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.ProductId == limited.Id && v.ColorId == color.Id);
    Check(await Reserve(added.Id, 500), "Unlimited override works on a finite-stock product");
    Check(await ApplyBulk(BulkStockMode.Unavailable) == 2 && !await Reserve(available.Id, 1)
        && !await Reserve(added.Id, 1), "Bulk unavailable blocks both combinations");
    Check(await db.ProductVariants.AnyAsync(v => v.Id == finite.Id && v.IsEnabled && v.Stock == 3
        && v.UnlimitedStockOverride == null), "Bulk stock preserves other colors and sizes");
    await db.ProductVariants.Where(v => v.Id == available.Id).ExecuteUpdateAsync(set => set
        .SetProperty(v => v.IsEnabled, true).SetProperty(v => v.Stock, 4)
        .SetProperty(v => v.UnlimitedStockOverride, (bool?)null));
    Check(await Reserve(available.Id, 20000), "Checkout accepts unlimited quantities");
    Check(await db.ProductVariants.Where(v => v.Id == available.Id).Select(v => v.Stock).SingleAsync() == 4, "Unlimited checkout never decrements stock");
    Check(!await Reserve(disabled.Id, 1), "Checkout rejects disabled historical variants");
    Check(await Reserve(finite.Id, 2, commit: false) && await db.ProductVariants.Where(v => v.Id == finite.Id).Select(v => v.Stock).SingleAsync() == 3, "Order transaction rollback restores finite stock");
    Check(await Reserve(finite.Id, 2) && await db.ProductVariants.Where(v => v.Id == finite.Id).Select(v => v.Stock).SingleAsync() == 1, "Finite checkout decrements stock exactly once");
    var attempts = await Task.WhenAll(Reserve(finite.Id, 1), Reserve(finite.Id, 1));
    Check(attempts.Count(ok => ok) == 1 && await db.ProductVariants.Where(v => v.Id == finite.Id).Select(v => v.Stock).SingleAsync() == 0, "Concurrent checkouts cannot oversell the final finite item");
    Check(!await Reserve(finite.Id, 1), "Zero finite stock blocks checkout");
    await db.Products.Where(p => p.Id == unlimited.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
    Check(!await Reserve(available.Id, 1), "Inactive unlimited products cannot be ordered");
    Check(!db.Database.HasPendingModelChanges(), "Inventory migration snapshot matches the model");
}
finally
{
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
}
Console.WriteLine($"{checks} inventory checks passed.");
sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
