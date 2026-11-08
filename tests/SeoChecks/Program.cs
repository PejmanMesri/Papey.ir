using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
    Check((await seo.ReadAsync("https://papey.ir/")).Title!.Contains("تیشرت چاپی"), "Homepage targets printed T-shirts naturally");
    Check((await seo.ReadAsync("https://papey.ir/custom-design")).Title!.Contains("سفارش تیشرت کاستوم"), "Custom order page has its own search intent");
    Check((await seo.ReadAsync("https://papey.ir/guides/tshirt-print-design")).Indexable, "Design guide is indexable");
    Check((await seo.ReadAsync("https://papey.ir/printed-hoodies")).Title!.Contains("خرید هودی چاپی"),"Hoodie landing page targets its own shopping intent");
    Check(!(await seo.ReadAsync("https://papey.ir/products?type=99999")).Indexable, "Invalid clothing types are not indexable");
    Check(!(await seo.ReadAsync("https://papey.ir/category/empty-category")).Indexable, "Empty categories are not indexable");
    Check(!(await seo.ReadAsync("https://papey.ir/products?sort=price")).Indexable, "Faceted sort pages cannot create duplicate indexed catalogs");
    using(var breadcrumbs=JsonDocument.Parse(page.SiteJson!)) Check(breadcrumbs.RootElement.GetProperty("@type").GetString()=="BreadcrumbList"&&breadcrumbs.RootElement.GetProperty("itemListElement")[1].GetProperty("@type").GetString()=="ListItem", "Breadcrumb JSON-LD uses valid schema types and canonical URLs");
    var sitemap = XDocument.Parse(await seo.SitemapAsync());
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var urls = sitemap.Descendants(ns + "loc").Select(x => x.Value).ToArray();
    Check(urls.Contains("https://papey.ir/product/test-product") && urls.Contains("https://papey.ir/category/test-category"), "Sitemap includes active products and their categories");
    Check(urls.Contains("https://papey.ir/printed-hoodies")&&urls.Contains("https://papey.ir/printed-tshirts")&&urls.Contains("https://papey.ir/guides/tshirt-print-design"),"Search landing page and guide appear in the sitemap");
    XNamespace imageNs="http://www.google.com/schemas/sitemap-image/1.1";
    Check(sitemap.Descendants(imageNs+"loc").Any(x=>x.Value=="https://papey.ir/uploads/test.jpg"),"Image sitemap includes the real active product photo");
    Check(!urls.Any(u => u.Contains("hidden") || u.Contains("empty-category") || u.Contains("/account")), "Sitemap excludes inactive products, empty categories and private pages");
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
    var start=new ProcessStartInfo("dotnet") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.GetFullPath("bin/Debug/net10.0/Papey.dll"));start.ArgumentList.Add("--urls");start.ArgumentList.Add($"http://127.0.0.1:{port}");
    var keyPath=Path.Combine(Path.GetTempPath(),"papey-seo-keys-"+Guid.NewGuid().ToString("N"));
    foreach(var pair in new Dictionary<string,string>{["ConnectionStrings__DefaultConnection"]=source.ConnectionString,["Deployment__HttpsOnly"]="false",["Deployment__RequireSecureConfiguration"]="false",["ASPNETCORE_ENVIRONMENT"]="Production",["DataProtection__KeyPath"]=keyPath,["DataProtection__ApplicationName"]="SeoChecks",["ADMIN_USERNAME"]="seo-test-owner",["ADMIN_PASSWORD"]="DisposableSeoOwner2026!",["SMSIR_SANDBOX"]="true",["SMSIR_SANDBOX_API_KEY"]="YOUR_DISABLED_TEST_KEY",["SMSIR_API_KEY"]="YOUR_DISABLED_TEST_KEY",["ZARINPAL_MERCHANT_ID"]="",["ZARINPAL_SANDBOX"]="true"})start.Environment[pair.Key]=pair.Value;
    using var server=Process.Start(start)!;var output=server.StandardOutput.ReadToEndAsync();var errors=server.StandardError.ReadToEndAsync();
    try {
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}")};
        for(var i=0;i<100;i++){try{if((await client.GetAsync("/healthz")).IsSuccessStatusCode)break;}catch{}if(server.HasExited)throw new Exception("SEO test server failed");await Task.Delay(150);}
        foreach(var path in new[]{"/product/missing-product","/category/missing-category"}){var response=await client.GetAsync(path);Check(response.StatusCode==HttpStatusCode.NotFound,"Missing catalog URL returns HTTP 404: "+path+" ("+(int)response.StatusCode+")");}
        var invalid=await client.GetStringAsync("/products?type=999999");Check(invalid.Contains("noindex,follow"),"Invalid filter is noindex in server-rendered HTML");
        foreach(var path in new[]{"/","/printed-tshirts","/printed-hoodies","/guides/tshirt-print-design","/custom-design","/product/test-product"}){
            var response=await client.GetAsync(path);var body=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode&&body.Contains("rel=\"canonical\"")&&body.Contains("name=\"description\""),"Public page has server-rendered SEO metadata: "+path);
            Check(System.Text.RegularExpressions.Regex.Matches(body,"<title>").Count==1,"One title element for "+path);
        }
        var contact=await client.GetAsync("/contact");Check(contact.StatusCode==HttpStatusCode.MovedPermanently&&contact.Headers.Location?.OriginalString=="/about#contact","Old contact URL permanently redirects to combined page");
        var slash=await client.GetAsync("/products/");Check(slash.StatusCode==HttpStatusCode.MovedPermanently,"Trailing-slash catalog duplicate redirects permanently");
    } finally {if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();}if(Directory.Exists(keyPath)&&Path.GetFileName(keyPath).StartsWith("papey-seo-keys-"))Directory.Delete(keyPath,true);}
    item.IsActive = false; await db.SaveChangesAsync();
    Check(!(await seo.SitemapAsync()).Contains("test-product"), "Sitemap updates after product visibility changes");
}
finally { await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync(); }

sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
