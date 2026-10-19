using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var connection = DatabaseConnection.Build(config);
if (!new NpgsqlConnectionStringBuilder(connection).Database!.StartsWith("papey_tax_review_"))
    throw new InvalidOperationException("Use a new disposable papey_tax_review_* database; never production.");
var factory = new Factory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
await using var db = factory.CreateDbContext();
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
await db.GetService<IMigrator>().MigrateAsync("20261019093612_ProductSizeGuideImage");
await db.Database.ExecuteSqlRawAsync("""
    INSERT INTO "Orders" ("OrderNumber","CustomerName","CustomerPhone","Province","City","FullAddress","TotalAmount","ShippingCost","GrandTotal","Status","CreatedAt")
    VALUES ('LEGACY-TAX-CHECK','Historical customer','09120000000','Test','Test','Test',1000000,0,1000000,5,CURRENT_TIMESTAMP)
    """);
await db.Database.MigrateAsync();
var legacy = await db.Orders.SingleAsync();
Check(!legacy.TaxEnabled && legacy.TaxRatePercent == 0 && legacy.TaxAmount == 0 && legacy.GrandTotal == 1000000,
    "Migration preserves old totals and identifies historical orders as untaxed");
var settings = new TaxSettingsService(factory);
Check(!await settings.ReadAsync(), "The store starts with tax off");
var notified = 0; settings.Changed += () => notified++;
await settings.UpdateAsync(true);
Check(await settings.ReadAsync() && notified == 1, "Global tax changes persist and notify open carts");
Check(OrderTax.Calculate(1000000, true) == 100000 && OrderTax.Calculate(1000000, false) == 0,
    "Ten percent applies to the goods subtotal only when enabled");
Check(OrderTax.Calculate(1005, true) == 101 && OrderTax.Calculate(1004, true) == 100,
    "Tax rounds to whole toman, with half-toman rounded up");
var buyer = new AppUser { Username = "Admin", FullName = "مدیر تست", IsAdmin = true,
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("DraftReview!20261018") };
db.Users.Add(buyer);
var category = new Category { Name = "Test", Slug = "tax-review" };
var product = new Product { Name = "محصول تست مالیات", Slug = "tax-review-product", Category = category, Price = 1000000, ImageUrl = "/img/products/damavand.svg" };
var variant = new ProductVariant { Product = product, Color = "مشکی", ColorHex = "#000", Size = "M", IsEnabled = true };
db.ProductVariants.Add(variant); await db.SaveChangesAsync();
Order New(string number, decimal subtotal) => new() { OrderNumber = number, UserId = buyer.Id,
    CustomerName = "مشتری تست", CustomerPhone = "09120000000", Province = "تهران", City = "تهران", FullAddress = "نشانی تست",
    TotalAmount = subtotal, Status = OrderStatus.Cancelled };
var taxed = New("WITH-TAX-CHECK", 1000000); taxed.ShippingCost = 25000;
taxed.Items.Add(new OrderItem { ProductVariantId = variant.Id, ProductId = product.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, UnitPrice = 1000000, Quantity = 1, LineTotal = 1000000 });
OrderTax.Apply(taxed, true); db.Orders.Add(taxed); await db.SaveChangesAsync();
Check(taxed.TaxRatePercent == 10 && taxed.TaxAmount == 100000 && taxed.GrandTotal == 1125000,
    "The saved snapshot adds tax once and does not tax shipping");
Check((long)(taxed.GrandTotal * 10) == 11250000, "Payment uses the tax-inclusive snapshot converted to rials once");
await settings.UpdateAsync(false);
var untaxed = New("WITHOUT-TAX-CHECK", 1000000);
OrderTax.Apply(untaxed, await settings.ReadAsync()); db.Orders.Add(untaxed); await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var saved = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == taxed.Id);
Check(saved.TaxEnabled && saved.TaxRatePercent == 10 && saved.TaxAmount == 100000 && saved.GrandTotal == 1125000,
    "Turning tax off cannot change a previously taxed invoice");
Check(!untaxed.TaxEnabled && untaxed.TaxAmount == 0 && untaxed.GrandTotal == 1000000,
    "Orders placed after disabling tax remain untaxed");
await settings.UpdateAsync(true);
Check(!(await db.Orders.AsNoTracking().SingleAsync(o => o.Id == untaxed.Id)).TaxEnabled,
    "Turning tax on cannot retroactively tax an older untaxed invoice");
var split = New("ROUNDING", 15);
split.Items = [new() { LineTotal = 5 }, new() { LineTotal = 5 }, new() { LineTotal = 5 }];
OrderTax.Apply(split, true);
var allocations = OrderTax.Allocate(split);
Check(split.TaxAmount == 2 && allocations.Sum() == 2 && allocations.All(a => a >= 0),
    "Official-invoice line taxes reconcile exactly with rounded order tax");
await using (var locked = factory.CreateDbContext())
await using (var transaction = await locked.Database.BeginTransactionAsync())
{
    Check(await TaxSettingsService.ReadForOrderAsync(locked), "Checkout reads the global tax policy under an order transaction");
    var toggle = settings.UpdateAsync(false);
    await Task.Delay(150);
    Check(!toggle.IsCompleted, "An admin toggle waits for an accepted checkout snapshot to commit");
    await transaction.CommitAsync(); await toggle;
}
Check(!await settings.ReadAsync(), "The pending toggle applies after checkout commits");
Check(!db.Database.HasPendingModelChanges(), "Tax migration matches the current model");
legacy = await db.Orders.SingleAsync(o => o.OrderNumber == "LEGACY-TAX-CHECK");
legacy.UserId = buyer.Id; await db.SaveChangesAsync();
Console.WriteLine($"{checks} tax checks passed; disposable fixtures remain for UI review.");

sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
