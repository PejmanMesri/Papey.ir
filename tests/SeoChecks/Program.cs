using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS: " + text); }
var product = new Product { Id = 42, Name = "A </script> test", Price = 250000, IsActive = true, HasUnlimitedStock = false };
product.Variants.Add(new() { Product = product, IsEnabled = true, Stock = 2 });
using (var json = JsonDocument.Parse(StoreSeo.ProductJson(product, "https://papey.ir/product/test", "https://papey.ir/test.jpg")))
{
    var offer = json.RootElement.GetProperty("offers");
    Check(offer.GetProperty("priceCurrency").GetString() == "IRR" && offer.GetProperty("price").GetDecimal() == 2500000m, "Schema converts toman to rial without changing storefront prices");
    Check(offer.GetProperty("availability").GetString()!.EndsWith("InStock"), "Enabled stock is available");
    Check(!StoreSeo.ProductJson(product, "url", null).Contains("</script>"), "JSON-LD safely escapes markup in product names");
}
product.Variants[0].Stock = 0;
Check(StoreSeo.ProductJson(product, "url", null).Contains("OutOfStock"), "Zero limited stock is unavailable");
product.HasUnlimitedStock = true;
Check(StoreSeo.ProductJson(product, "url", null).Contains("InStock"), "Unlimited enabled variants remain available");
product.Variants.Add(new() { Product = product, IsEnabled = true, PriceOverride = 300000 });
using (var json = JsonDocument.Parse(StoreSeo.ProductJson(product, "url", null)))
    Check(json.RootElement.GetProperty("offers").GetProperty("highPrice").GetDecimal() == 3000000m, "Variant prices produce an accurate offer range");
Check(StoreSeo.Description(new string('x', 200), "fallback").Length <= 160, "Descriptions are bounded");

var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? throw new Exception("Explicit test connection required")) { Pooling = false };
var database = "papey_seo_checks_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
source.Database = database;
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options;
try
{
    await using var db = new AppDbContext(options);
    await db.Database.MigrateAsync();
    var category = new Category { Name = "Test category", Slug = "test-category", IsActive = true };
    var empty = new Category { Name = "Empty", Slug = "empty-category", IsActive = true };
    var item = new Product { Name = "Test product", Slug = "test-product", Price = 123000, Category = category, ImageUrl = "/uploads/test.jpg" };
    item.Variants.Add(new() { Color = "Black", Size = "M", Stock = 1 });
    db.Categories.Add(empty);
    db.Products.Add(item);
    db.Products.Add(new() { Name = "Hidden", Slug = "hidden", Category = category, IsActive = false });
    await db.SaveChangesAsync();
    var seo = new StoreSeo(new Factory(options), new ConfigurationBuilder().Build());
    var page = await seo.ReadAsync("https://untrusted-host.example/product/test-product?utm_source=test");
    Check(page.Indexable && page.Canonical == "https://papey.ir/product/test-product" && page.Image == "https://papey.ir/uploads/test.jpg", "Canonical and preview URLs use the configured site, stripping campaign parameters");
    Check(page.Title!.Contains("Test product") && page.ProductJson is not null, "Product-specific title and structured data are generated");
    Check(!(await seo.ReadAsync("https://papey.ir/product/hidden")).Indexable && !(await seo.ReadAsync("https://papey.ir/account/orders")).Indexable, "Hidden products and account pages are not indexable");
    var sitemap = XDocument.Parse(await seo.SitemapAsync());
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var urls = sitemap.Descendants(ns + "loc").Select(x => x.Value).ToArray();
    Check(urls.Contains("https://papey.ir/product/test-product") && urls.Contains("https://papey.ir/category/test-category"), "Sitemap includes active products and their categories");
    Check(!urls.Any(u => u.Contains("hidden") || u.Contains("empty-category") || u.Contains("/account")), "Sitemap excludes inactive products, empty categories and private pages");
    item.IsActive = false; await db.SaveChangesAsync();
    Check(!(await seo.SitemapAsync()).Contains("test-product"), "Sitemap updates after product visibility changes");
}
finally { await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync(); }

sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
