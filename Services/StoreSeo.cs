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
        var available = variants.Any(v => product.IsActive && (ProductInventory.IsUnlimited(v, product) || v.Stock > 0));
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
        var description = "خرید تیشرت چاپی و هودی چاپی از پاپی؛ سفارش لباس چاپی با طرح دلخواه، انتخاب رنگ و سایز، بررسی فایل طراحی و هماهنگی قیمت برای تیشرت کاستوم.";
        await using var db = await factory.CreateDbContextAsync();
        if (path == "") title = "پاپی | خرید تیشرت چاپی، هودی چاپی و لباس کاستوم";
        else if (path == "/custom-design") { title = "سفارش تیشرت کاستوم و لباس چاپی | پاپی"; description = "سفارش تیشرت چاپی، هودی کاستوم و لباس با طرح دلخواه در پاپی؛ فایل طراحی، رنگ، سایز و محل چاپ را هماهنگ کنید. قیمت و زمان پیش از نهایی شدن سفارش اعلام می‌شود."; image = Url("/img/custom-studio/hoodie-white-front.webp"); }
        else if (path == "/printed-tshirts") { title="خرید تیشرت چاپی با طرح‌های خاص | پاپی"; description="مدل‌های تیشرت چاپی پاپی را ببینید؛ طرح‌های انیمه، موسیقی، گیم و تاریخی با انتخاب رنگ و سایز. برای چاپ طرح شخصی، سفارش تیشرت کاستوم ثبت کنید."; }
        else if (path == "/printed-hoodies") { title="خرید هودی چاپی و سفارش هودی کاستوم | پاپی"; description="مدل‌های هودی چاپی پاپی را ببینید یا برای سفارش هودی چاپی با طرح دلخواه هماهنگ کنید؛ انتخاب رنگ و سایز، راهنمای فایل طراحی و بررسی محل چاپ روی هودی."; image=Url("/img/custom-studio/hoodie-black-front.webp"); }
        else if (path == "/guides/dtf-printing") { title="چاپ DTF روی لباس، تیشرت و هودی | پاپی"; description="چاپ DTF یا دی‌تی‌اف در پاپی؛ خرید تیشرت DTF و هودی چاپی، سفارش لباس DTF با طرح دلخواه و راهنمای آماده‌سازی فایل و هماهنگی قیمت پیش از سفارش."; image=Url("/img/custom-studio/tshirt-black-front.webp"); }
        else if (path == "/guides/tshirt-print-design") { title="راهنمای طراحی تیشرت چاپی و آماده‌سازی فایل | پاپی"; description="برای طراحی تیشرت چاپی از کجا شروع کنیم؟ راهنمای انتخاب طرح، پس‌زمینه شفاف، محل چاپ، رنگ لباس و ارسال فایل برای سفارش تیشرت کاستوم در پاپی."; }
        else if (path == "/about") { title = "درباره پاپی؛ پارچه‌ها، کیفیت و راه‌های ارتباط"; description = "معرفی پارچه‌ها و متریال پوشاک پاپی؛ آشنایی با دورس دو نخ و سه نخ، انتخاب پارچه متناسب با سفارش، تعهد به کیفیت تولید و راه‌های ارتباط با مجموعه."; }
        else if (path == "/wholesale") { title = "تولید و پخش عمده پوشاک سفارشی | پاپی"; description = "تولید و خرید عمده پوشاک پاپی با تجربه بیست ساله؛ تیشرت، هودی، دورس، پیراهن، شلوار و کاپشن سفارشی، پوشاک سازمانی و ورزشی. مشاوره: ۰۹۱۲۱۸۳۸۹۹۱."; }
        else if (path == "/products")
        {
            title = "همه محصولات | پاپی";
            if (int.TryParse(QueryHelpers.ParseQuery(uri.Query).GetValueOrDefault("type"), out var typeId))
            {
                var type = await db.ProductTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId && t.IsActive);
                if (type is not null) { title = $"خرید {ClothingSearchTerms.PrintedName(type.Name)} با طرح‌های خاص | پاپی"; description=$"مدل‌های {ClothingSearchTerms.PrintedName(type.Name)} پاپی را ببینید و طرح، رنگ و سایز دلخواه خود را انتخاب کنید؛ سفارش آنلاین و ارسال با تیپاکس."; canonical += $"?type={typeId}";
                    if(!await db.Products.AnyAsync(p=>p.IsActive&&(p.ProductTypeId ?? p.Category.ProductTypeId)==typeId)) title=null;
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
                title = $"{(category.ProductType is null ? "لباس چاپی" : ClothingSearchTerms.PrintedName(category.ProductType.Name))} طرح {category.Name} | پاپی".Trim();
                description = Description(category.Description, $"خرید {(category.ProductType is null ? "لباس چاپی" : ClothingSearchTerms.PrintedName(category.ProductType.Name))} با طرح {category.Name} از پاپی؛ انتخاب مدل، رنگ و سایز مورد علاقه شما.");
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
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Slug, p.CategoryId, ProductTypeId=p.ProductTypeId ?? p.Category.ProductTypeId }).ToListAsync();
        var categoryIds = products.Select(p => p.CategoryId).ToHashSet();
        categoryIds.UnionWith(await db.ProductCategories.Where(pc => pc.Product.IsActive).Select(pc => pc.CategoryId).ToListAsync());
        var categories = await db.Categories.Where(c => c.IsActive && categoryIds.Contains(c.Id)).Select(c => c.Slug).ToListAsync();
        var typeIds = products.Where(p => p.ProductTypeId != null).Select(p => p.ProductTypeId!.Value).ToHashSet();
        var types = await db.ProductTypes.Where(t => t.IsActive && typeIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        var paths = new[] { "/", "/products", "/printed-tshirts", "/printed-hoodies", "/guides/dtf-printing", "/guides/tshirt-print-design", "/about", "/wholesale", "/custom-design" }.Concat(products.Select(p => "/product/" + Uri.EscapeDataString(p.Slug)))
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
