using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
DotNetEnv.Env.NoClobber().TraversePath().Load();
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? DatabaseConnection.Build(config)) { Pooling = false };
var name = "papey_catalog_checks_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin).ExecuteNonQueryAsync();
source.Database = name;
try
{
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
    await db.GetService<IMigrator>().MigrateAsync("20261017151109_AddUnlimitedProductStock");
    var category = new Category { Name = "Custom category", Slug = "custom-category" };
    db.Categories.Add(category); await db.SaveChangesAsync();
    async Task Insert(string slug, string image) => await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "Products" ("Name", "Slug", "Price", "ImageUrl", "CategoryId", "IsFeatured", "IsNew", "IsActive", "CreatedAt")
        VALUES ({slug}, {slug}, 100, {image}, {category.Id}, FALSE, FALSE, TRUE, CURRENT_TIMESTAMP)
        """);
    await Insert("tshirt-anime-shadow", "/img/products/damavand.svg");
    await Insert("hoodie-anime-hero", "/img/products/pinterest-vintage.svg");
    await Insert("tshirt-minimal", "/uploads/my-own-product.jpg");
    var ids = await db.Database.SqlQueryRaw<int>("SELECT \"Id\" AS \"Value\" FROM \"Products\" ORDER BY \"Id\"").ToListAsync();
    var variant = new ProductVariant { ProductId = ids[1], Color = "Black", Size = "M" };
    db.ProductVariants.Add(variant); await db.SaveChangesAsync();
    var order = new Order { OrderNumber = "PRESERVE-ORDER", CustomerName = "Customer", CustomerPhone = "09123456789", Province = "Test", City = "Test", FullAddress = "Test" };
    order.Items.Add(new OrderItem { ProductId = ids[1], ProductVariantId = variant.Id, ProductName = "Historical product", Color = "Black", Size = "M", Quantity = 1 });
    db.Orders.Add(order); await db.SaveChangesAsync();
    await db.Database.MigrateAsync();
    db.ChangeTracker.Clear();
    Check(!await db.Products.AnyAsync(p => p.Id == ids[0]), "Unused sample products are removed by the migration");
    Check(await db.Products.AnyAsync(p => p.Id == ids[1] && !p.IsActive) && await db.OrderItems.CountAsync() == 1,
        "Samples referenced by orders are archived without changing order history");
    Check(await db.Products.AnyAsync(p => p.Id == ids[2] && p.IsActive), "Customized products sharing a sample slug are preserved");
    var count = await db.Products.CountAsync();
    await DbSeeder.EnsureStoreSeededAsync(db); await DbSeeder.EnsureStoreSeededAsync(db);
    Check(await db.Products.CountAsync() == count && await db.Orders.CountAsync() == 1,
        "Repeated startup never recreates demo products or resets existing orders");
    await db.Products.Where(p => p.Id == ids[2]).ExecuteUpdateAsync(s => s.SetProperty(p => p.HomeHeroSlot, 1));
    async Task<bool> RejectSlot(int id, int slot, string state)
    {
        try { await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.HomeHeroSlot, slot)); return false; }
        catch (PostgresException ex) when (ex.SqlState == state) { return true; }
    }
    Check(await RejectSlot(ids[1], 1, PostgresErrorCodes.UniqueViolation), "Only one product can occupy each homepage slot");
    Check(await RejectSlot(ids[1], 3, PostgresErrorCodes.CheckViolation), "Homepage slots are limited to first and second");
    Check(!db.Database.HasPendingModelChanges(), "Catalog migration matches the model");
    Console.WriteLine($"{checks} catalog checks passed.");
}
finally { await new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin).ExecuteNonQueryAsync(); }
