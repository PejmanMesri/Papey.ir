using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? throw new Exception("Explicit disposable test connection required")) { Pooling = false };
var database = "papey_review_checks_" + Guid.NewGuid().ToString("N");
await using var adminConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await adminConnection.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection).ExecuteNonQueryAsync();
source.Database = database;
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options;
var factory = new Factory(options);
try
{
    await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
    var shirtType = new ProductTypeEntity { Name = "تیشرت" }; var hoodieType = new ProductTypeEntity { Name = "هودی" }; var longType = new ProductTypeEntity { Name = "لانگ اسلیو" };
    var category = new Category { Name = "انیمه", Slug = "anime", ProductType = shirtType };
    Product Product(string name, ProductTypeEntity type, bool active = true) => new() { Name = name, Slug = Guid.NewGuid().ToString("N"), ProductType = type, Category = category, IsActive = active,
        Variants = [new() { Color = "Black", Size = "M", IsEnabled = true }] };
    var shirt = Product("تیشرت لوفی", shirtType); var hoodie = Product("هودی لوفی", hoodieType); var longsleeve = Product("لانگ اسلیو لوفی", longType);
    var similar = Product("تیشرت طرح دیگر", shirtType); var hidden = Product("هودی لوفی", hoodieType, false);
    db.Products.AddRange(shirt, hoodie, longsleeve, similar, hidden);
    var buyer = new AppUser { Username = "buyer", FullName = "علی رضایی", Phone = "09123456789", PasswordHash = "not-a-real-password" };
    var stranger = new AppUser { Username = "stranger", PasswordHash = "unused" };
    var admin = new AppUser { Username = "admin", IsAdmin = true, PasswordHash = "unused" };
    db.Users.AddRange(buyer, stranger, admin); await db.SaveChangesAsync();
    var recommendations = new ProductRecommendations(factory);
    var related = await recommendations.ReadAsync(shirt.Id);
    Check(related.SameDesign.Select(p => p.Id).ToHashSet().SetEquals([hoodie.Id, longsleeve.Id]), "Existing name matching finds the same design on other clothing types");
    Check(related.Similar.Any(p => p.Id == similar.Id) && !related.Similar.Any(p => related.SameDesign.Any(s => s.Id == p.Id)), "Similar recommendations have no duplicate cross-model cards");
    Check(!related.SameDesign.Concat(related.Similar).Any(p => p.Id == shirt.Id || !p.IsActive), "Recommendations exclude the current and inactive products");
    Check((await recommendations.ReadAsync(hidden.Id)).SameDesign.Count == 0, "Inactive source products produce no recommendations");
    shirt.DesignCode = "luffy-01"; hoodie.DesignCode = "LUFFY-01"; hoodie.Name = "یک نام متفاوت"; await db.SaveChangesAsync();
    Check((await recommendations.ReadAsync(shirt.Id)).SameDesign.Single().Id == hoodie.Id, "Explicit design codes reliably link products with different names");
    var config = new ConfigurationBuilder().Build(); var environment = new TestEnvironment();
    var sms = new SmsService(new HttpClient(new NoNetwork()), config, environment, NullLogger<SmsService>.Instance);
    ProductReviewService Service(int? id, bool claimAdmin = false)
    {
        var context = new DefaultHttpContext();
        if (id is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.Value.ToString()), new Claim(ClaimTypes.Role, claimAdmin ? "Admin" : "User")], "test"));
        return new(factory, new AuthService(factory, new FixedAccessor { HttpContext = context }, config, sms, environment));
    }
    Order Purchase(AppUser user, Product product, OrderStatus status = OrderStatus.Paid, bool timestamp = true) => new() { User = user, CustomerName = "Test buyer", CustomerPhone = "09123456789", Province = "Test", City = "Test", FullAddress = "Test",
        OrderNumber = Guid.NewGuid().ToString("N")[..18], Status = status, PaidAt = timestamp ? DateTime.UtcNow : null,
        Items = [new() { Product = product, ProductVariant = product.Variants[0], ProductName = product.Name, Color = "Black", Size = "M", Quantity = 1 }] };
    var input = new ReviewInput(5, 4, ReviewFit.TrueToSize, "Great print and fabric <script>alert(1)</script>");
    var anonymous = Service(null);
    Check(!await anonymous.CanReviewAsync(shirt.Id) && await anonymous.SaveAsync(shirt.Id, input) is not null, "Anonymous visitors cannot create reviews");
    var customer = Service(buyer.Id); var outsider = Service(stranger.Id);
    Check(!await customer.CanReviewAsync(shirt.Id) && await customer.SaveAsync(shirt.Id, input) is not null, "An account without a purchase cannot review");
    db.Orders.Add(Purchase(buyer, shirt, OrderStatus.Pending, false)); db.Orders.Add(Purchase(buyer, hoodie)); await db.SaveChangesAsync();
    Check(!await customer.CanReviewAsync(shirt.Id), "An unpaid order or purchase of a different model cannot verify a product review");
    db.Orders.Add(Purchase(buyer, shirt, OrderStatus.Paid, false)); await db.SaveChangesAsync();
    Check(!await customer.CanReviewAsync(shirt.Id), "A status without confirmed payment cannot verify a buyer");
    var paid = Purchase(buyer, shirt); db.Orders.Add(paid); await db.SaveChangesAsync();
    Check(await customer.CanReviewAsync(shirt.Id), "A confirmed payment for this exact product grants review access");
    Check(await customer.SaveAsync(shirt.Id, input with { PrintQuality = 6 }) is not null && await customer.SaveAsync(shirt.Id, input with { Fit = (ReviewFit)99 }) is not null, "Invalid scores and fit values are rejected server-side");
    Check(await customer.SaveAsync(shirt.Id, input with { Comment = new string('x', 2001) }) is not null, "Oversized review text is rejected");
    Check(await customer.SaveAsync(shirt.Id, input) is null, "Verified purchase can submit a review");
    var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => customer.SaveAsync(shirt.Id, input)));
    Check(results.All(r => r is null) && await db.ProductReviews.CountAsync() == 1, "Concurrent submissions update one review rather than creating duplicates");
    var published = await customer.ReadAsync(shirt.Id); var reviewId = published.Reviews.Single().Id;
    Check(published.Count == 1 && published.Average == 4.5 && published.Reviews[0].Name == "علی", "Public reviews have accurate ratings and reveal only the buyer's first name");
    Check(await outsider.MineAsync(shirt.Id) is null && await outsider.SaveAsync(shirt.Id, input) is not null, "Other customers cannot edit or impersonate the buyer");
    Check(!await Service(stranger.Id, claimAdmin: true).ReplyAsync(reviewId, "Fake admin") && !await outsider.SetHiddenAsync(reviewId, true), "Reply and moderation privileges require a real administrator in the database");
    var manager = Service(admin.Id, claimAdmin: true);
    Check(await manager.ReplyAsync(reviewId, "Thank you for your purchase"), "Administrator can answer a review");
    Check((await customer.ReadAsync(shirt.Id)).Reviews.Single().Reply == "Thank you for your purchase", "Admin response appears beside the public review");
    Check(await manager.SetHiddenAsync(reviewId, true) && (await customer.ReadAsync(shirt.Id)).Count == 0, "Hidden reviews are excluded from public counts and scores");
    await customer.SaveAsync(shirt.Id, input with { Comment = "Updated review" });
    Check((await customer.ReadAsync(shirt.Id)).Count == 0 && (await customer.MineAsync(shirt.Id))!.IsHidden, "Customer edits do not bypass admin moderation");
    await manager.SetHiddenAsync(reviewId, false);
    Check((await customer.ReadAsync(shirt.Id)).Count == 1, "Admin can restore a hidden review");
    paid.Status = OrderStatus.Cancelled; await db.SaveChangesAsync();
    Check((await customer.ReadAsync(shirt.Id)).Count == 0 && !await customer.CanReviewAsync(shirt.Id), "Cancelled purchases cannot retain a verified-buyer badge");
    Check(!db.Database.HasPendingModelChanges(), "Review migration matches the current database model");
    Console.WriteLine($"{checks} review and recommendation checks passed.");
}
finally { await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", adminConnection).ExecuteNonQueryAsync(); }

sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext() => new(options); }
sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = "Development"; public string ApplicationName { get; set; } = "Tests"; public string ContentRootPath { get; set; } = Path.GetTempPath(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new Exception("No network in review tests"); }

sealed class FixedAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
