using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed record SeoPage(string? Title, string Description, string Canonical, string? Image, string? ProductJson, bool Indexable, string? SiteJson = null);

public sealed class StoreSeo(IDbContextFactory<AppDbContext> factory, IConfiguration configuration)
{
    public string Origin => (configuration["SITE_URL"] ?? "https://papey.ir").TrimEnd('/');
    public string Url(string path) => Origin + (path.StartsWith('/') ? path : "/" + path);
    public string? ImageUrl(string? path) => string.IsNullOrWhiteSpace(path) ? null
        : Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri
        : path.StartsWith('/') && !path.StartsWith("//") ? Url(path) : null;
    public static string Description(string? text, string fallback)
    {
        var value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (value.Length == 0) value = fallback;
        return value.Length <= 160 ? value : value[..157] + "…";
    }
    public static string ProductJson(Product product, string url, string? image, int discountPercent = 0)
    {
        var variants = product.Variants.Where(v => v.IsEnabled).ToArray();
        var prices = variants.Select(v => v.EffectivePrice).DefaultIfEmpty(product.Price).Select(price=>price-DiscountService.PercentOff(price,discountPercent)).ToArray();
        var available = variants.Any(v => product.IsActive && (product.HasUnlimitedStock || v.Stock > 0));
        var offers = new Dictionary<string, object?> {
            ["@type"] = prices.Min() == prices.Max() ? "Offer" : "AggregateOffer", ["url"] = url, ["priceCurrency"] = "IRR",
            ["availability"] = "https://schema.org/" + (available ? "InStock" : "OutOfStock"), ["itemCondition"] = "https://schema.org/NewCondition"
        };
        // Stored prices are toman; ISO currency IRR requires multiplying by ten.
        if (prices.Min() == prices.Max()) offers["price"] = prices.Min() * 10m;
        else { offers["lowPrice"] = prices.Min() * 10m; offers["highPrice"] = prices.Max() * 10m; offers["offerCount"] = variants.Length; }
        return JsonSerializer.Serialize(new Dictionary<string, object?> {
            ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = product.Name,
            ["description"] = Description(product.Description, $"خرید {product.Name} از پاپی"), ["sku"] = product.Id.ToString(), ["url"] = url,
            ["image"] = (image is null ? Array.Empty<string>() : new[] { image }).Concat(product.Media.Where(m=>m.Type==MediaType.Image).OrderBy(m=>m.DisplayOrder).Select(m=>Uri.TryCreate(new Uri(url),m.Url,out var mediaUri)?mediaUri.AbsoluteUri:null).OfType<string>()).Distinct().ToArray(),
            ["brand"] = new Dictionary<string, string> { ["@type"] = "Brand", ["name"] = "Papey" }, ["offers"] = offers
        });
    }
    public async Task<SeoPage> ReadAsync(string location)
    {
        var uri = new Uri(location);
        var path = uri.AbsolutePath.TrimEnd('/');
        var canonical = Url(path.Length == 0 ? "/" : path);
        string? title = null, image = null, json = null;
        var description = "خرید تیشرت چاپی با طرح‌های خاص و سفارش تیشرت کاستوم از پاپی؛ انتخاب مدل، رنگ و سایز، بررسی طرح دلخواه و ارسال با تیپاکس به سراسر ایران.";
        await using var db = await factory.CreateDbContextAsync();
        if (path == "") title = "پاپی | خرید تیشرت چاپی و سفارش تیشرت کاستوم";
        else if (path == "/custom-design") { title = "سفارش تیشرت کاستوم با طرح دلخواه | پاپی"; description = "برای سفارش تیشرت کاستوم و چاپ طرح دلخواه روی تیشرت، هودی یا لانگ اسلیو با پاپی هماهنگ کنید؛ بررسی فایل طراحی، انتخاب لباس و اعلام قیمت پیش از سفارش."; image = Url("/img/custom-studio/hoodie-white-front.webp"); }
        else if (path == "/printed-tshirts") { title="خرید تیشرت چاپی با طرح‌های خاص | پاپی"; description="مدل‌های تیشرت چاپی پاپی را ببینید؛ طرح‌های انیمه، موسیقی، گیم و تاریخی با انتخاب رنگ و سایز. برای چاپ طرح شخصی، سفارش تیشرت کاستوم ثبت کنید."; }
        else if (path == "/guides/tshirt-print-design") { title="راهنمای طراحی تیشرت چاپی و آماده‌سازی فایل | پاپی"; description="برای طراحی تیشرت چاپی از کجا شروع کنیم؟ راهنمای انتخاب طرح، پس‌زمینه شفاف، محل چاپ، رنگ لباس و ارسال فایل برای سفارش تیشرت کاستوم در پاپی."; }
        else if (path == "/about") { title = "درباره و ارتباط با ما | پاپی"; description = "با پاپی آشنا شوید؛ فروشگاه تیشرت، هودی و لانگ اسلیو با طرح‌های خاص و امکان سفارش لباس با طرح دلخواه."; }
        else if (path == "/wholesale") { title = "فروش عمده پوشاک و قرارداد همکاری | پاپی"; description = "برای سفارش عمده تیشرت، هودی و لانگ اسلیو، توافق قیمت و عقد قرارداد همکاری با پاپی تماس بگیرید: ۰۹۱۲۱۸۳۸۹۹۱."; }
        else if (path == "/products")
        {
            title = "همه محصولات | پاپی";
            if (int.TryParse(QueryHelpers.ParseQuery(uri.Query).GetValueOrDefault("type"), out var typeId))
            {
                var type = await db.ProductTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId && t.IsActive);
                if (type is not null) { title = $"خرید {type.Name} با طرح‌های خاص | پاپی"; description=$"مدل‌های {type.Name} پاپی را ببینید و طرح، رنگ و سایز دلخواه خود را انتخاب کنید؛ سفارش آنلاین و ارسال با تیپاکس."; canonical += $"?type={typeId}";
                    if(!await db.Products.AnyAsync(p=>p.IsActive&&p.ProductTypeId==typeId)) title=null;
                } else title=null;
            }
        }
        else if (path.StartsWith("/product/"))
        {
            var slug = Uri.UnescapeDataString(path[9..]);
            var product = await db.Products.AsNoTracking().Include(p => p.Variants).Include(p=>p.Media).FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive);
            if (product is not null)
            {
                title = $"{product.Name} | خرید از پاپی";
                description = Description(product.Description, $"خرید {product.Name} از پاپی؛ مشاهده تصاویر، رنگ‌ها و راهنمای سایز.");
                image = ImageUrl(product.ImageUrl); json = ProductJson(product, canonical, image, await db.StoreSettings.Where(s=>s.Id==1).Select(s=>s.DiscountPercent).SingleAsync());
            }
        }
        else if (path.StartsWith("/category/"))
        {
            var slug = Uri.UnescapeDataString(path[10..]);
            var category = await db.Categories.AsNoTracking().Include(c => c.ProductType).FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
            if (category is not null)
            {
                title = $"{category.ProductType?.Name} طرح {category.Name} | پاپی".Trim();
                description = Description(category.Description, $"خرید لباس با طرح {category.Name} از پاپی؛ انتخاب مدل، رنگ و سایز مورد علاقه شما.");
                image = ImageUrl(category.ImageUrl);
            }
        }
        else if (path == "/contact") { canonical = Url("/about"); title = "ارتباط با پاپی | تماس و پشتیبانی سفارش"; description = "راه‌های ارتباط با فروشگاه پاپی؛ تماس، واتساپ و اینستاگرام برای راهنمای خرید و پیگیری سفارش."; }
        var indexable = title is not null;
        var query=QueryHelpers.ParseQuery(uri.Query);
        if(path=="/products" && query.TryGetValue("type",out var rawType) && !int.TryParse(rawType,out _))indexable=false;
        if(query.Keys.Any(k=>new[]{"sort","q","minPrice","maxPrice","color","size"}.Contains(k,StringComparer.OrdinalIgnoreCase)))indexable=false;
        if(path.StartsWith("/category/") && indexable) {
            var slug=Uri.UnescapeDataString(path[10..]);
            indexable=await db.Products.AnyAsync(p=>p.IsActive&&(p.Category.Slug==slug||p.ProductCategories.Any(pc=>pc.Category.Slug==slug)));
        }
        if (indexable && image is null)
            image = ImageUrl(await db.Products.AsNoTracking().Where(p => p.IsActive && p.ImageUrl != null).OrderByDescending(p => p.HomeHeroSlot != null).ThenBy(p => p.Id).Select(p => p.ImageUrl).FirstOrDefaultAsync());
        var siteJson = path == "" ? JsonSerializer.Serialize(new Dictionary<string, object?> {
            ["@context"] = "https://schema.org", ["@graph"] = new object[] {
                new Dictionary<string, object?> { ["@type"] = "OnlineStore", ["@id"] = Url("/#organization"), ["name"] = "پاپی", ["alternateName"] = "Papey", ["url"] = Url("/"), ["sameAs"] = new[] { "https://www.instagram.com/papey.ir", "https://t.me/papeycompany" } },
                new Dictionary<string, object?> { ["@type"] = "WebSite", ["@id"] = Url("/#website"), ["name"] = "پاپی", ["alternateName"] = "Papey", ["url"] = Url("/"), ["inLanguage"] = "fa-IR" }
            }
        }) : null;
        if(indexable && path!="") {
            var name=title?.Split(" | ")[0]??"پاپی";
            siteJson=JsonSerializer.Serialize(new Dictionary<string,object?> {
                ["@context"]="https://schema.org",["@type"]="BreadcrumbList",["itemListElement"]=new object[]{
                    new Dictionary<string,object?> { ["@type"]="ListItem",["position"]=1,["name"]="پاپی",["item"]=Url("/") },
                    new Dictionary<string,object?> { ["@type"]="ListItem",["position"]=2,["name"]=name,["item"]=canonical }
                }
            });
        }
        if(title is null && (path.StartsWith("/product/")||path.StartsWith("/category/")))title="صفحه پیدا نشد | پاپی";
        return new(title, description, canonical, image, json, indexable, siteJson);
    }
    public async Task<string> SitemapAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Slug, p.CategoryId, p.ProductTypeId }).ToListAsync();
        var categoryIds = products.Select(p => p.CategoryId).ToHashSet();
        categoryIds.UnionWith(await db.ProductCategories.Where(pc => pc.Product.IsActive).Select(pc => pc.CategoryId).ToListAsync());
        var categories = await db.Categories.Where(c => c.IsActive && categoryIds.Contains(c.Id)).Select(c => c.Slug).ToListAsync();
        var typeIds = products.Where(p => p.ProductTypeId != null).Select(p => p.ProductTypeId!.Value).ToHashSet();
        var types = await db.ProductTypes.Where(t => t.IsActive && typeIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        var paths = new[] { "/", "/products", "/printed-tshirts", "/guides/tshirt-print-design", "/about", "/wholesale", "/custom-design" }.Concat(products.Select(p => "/product/" + Uri.EscapeDataString(p.Slug)))
            .Concat(categories.Select(c => "/category/" + Uri.EscapeDataString(c))).Concat(types.Select(t => $"/products?type={t}"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace img="http://www.google.com/schemas/sitemap-image/1.1";
        var images=await db.Products.AsNoTracking().Where(p=>p.IsActive).Select(p=>new {p.Slug,p.ImageUrl,Media=p.Media.Where(m=>m.Type==MediaType.Image).OrderBy(m=>m.DisplayOrder).Select(m=>m.Url).ToList()}).ToListAsync();
        return new XDocument(new XElement(ns+"urlset",new XAttribute(XNamespace.Xmlns+"image",img),paths.Distinct().Select(path=> {
            var element=new XElement(ns+"url",new XElement(ns+"loc",Url(path)));
            var product=images.FirstOrDefault(p=>path=="/product/"+Uri.EscapeDataString(p.Slug));
            if(product is not null) foreach(var image in product.Media.Prepend(product.ImageUrl).Select(ImageUrl).OfType<string>().Distinct().Take(20))element.Add(new XElement(img+"image",new XElement(img+"loc",image)));
            return element;
        }))).ToString();
    }
}
