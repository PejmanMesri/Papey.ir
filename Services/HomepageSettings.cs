using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed class HomeSlide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Image { get; set; } = "";
    public string Title { get; set; } = "سلیقه‌ات را زندگی کن.";
    public string Description { get; set; } = "طرح‌هایی از دنیای تو، روی لباسی برای هر روز تو.";
    public string Link { get; set; } = "/products";
    public bool Active { get; set; } = true;
    public bool IsCampaign { get; set; }
}
public sealed class HomepageContent
{
    public int IntervalSeconds { get; set; } = 6;
    public List<HomeSlide> Slides { get; set; } = [];
    public Dictionary<int, string> TypePhotos { get; set; } = [];
}

public sealed class HomepageSettings(IDbContextFactory<AppDbContext> factory, ProductDraftUploads uploads, HomepageChanges? changes = null)
{
    public long Revision { get; private set; }
    public static string? DefaultPhoto(string name) => name.Contains("هودی") ? "/img/custom-studio/hoodie-black-front.webp"
        : name.Contains("لانگ") ? "/img/custom-studio/long-sleeve-black-front.webp"
        : name.Contains("تیشرت") || name.Contains("تی‌شرت") ? "/img/custom-studio/tshirt-black-front.webp" : null;

    public async Task<HomepageContent> ReadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.StorePageContents.AsNoTracking().SingleOrDefaultAsync(p => p.Id == "homepage");
        if (row is not null) return JsonSerializer.Deserialize<HomepageContent>(row.ContentJson) ?? new();
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.HomeHeroSlot != null).OrderBy(p => p.HomeHeroSlot).Take(2).ToListAsync();
        if (products.Count == 0) products = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderByDescending(p => p.CreatedAt).Take(2).ToListAsync();
        return new() { Slides = products.Select(p => new HomeSlide { Image = p.ImageUrl ?? "", Title = p.Name, Link = "/product/" + p.Slug }).ToList() };
    }

    public async Task<string?> SaveAsync(ClaimsPrincipal? principal, HomepageContent content, IReadOnlyDictionary<string, ProductDraftUploads.Upload> pending)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await db.Users.SingleOrDefaultAsync(u => u.Id == id) : null;
        if (principal is null || user?.CanManageStore != true || !AccountAccess.Valid(principal, user, roles: true)) return "اجازه ویرایش ندارید.";
        if (content.Slides.Count > 20 || content.IntervalSeconds is < 3 or > 30) return "حداکثر ۲۰ اسلاید و زمان نمایش بین ۳ تا ۳۰ ثانیه است.";
        if (content.Slides.Select(s => s.Id).Distinct().Count() != content.Slides.Count) return "شناسه اسلاید تکراری است.";
        foreach (var slide in content.Slides)
        {
            if (slide.Title?.Length > 120 || slide.Description?.Length > 500) return "عنوان (حداکثر ۱۲۰ حرف) و توضیح (حداکثر ۵۰۰ حرف) را بررسی کنید.";
            if (!LocalUrl(slide.Link)) return "لینک دکمه باید آدرس داخلی مانند /products باشد.";
            if (!pending.ContainsKey(slide.Id) && !ImageUrl(slide.Image)) return "برای هر اسلاید یک تصویر انتخاب کنید.";
        }
        if (content.TypePhotos.Values.Any(v => !ImageUrl(v))) return "آدرس تصویر معتبر نیست.";
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(701127)");
        var copy = JsonSerializer.Deserialize<HomepageContent>(JsonSerializer.Serialize(content))!;
        var paths = new List<string>();
        try
        {
            foreach (var entry in pending)
            {
                var slide = copy.Slides.SingleOrDefault(s => s.Id == entry.Key);
                var typeId = entry.Key.StartsWith("type-") && int.TryParse(entry.Key[5..], out var type) ? type : 0;
                if (slide is null && (typeId == 0 || !await db.ProductTypes.AnyAsync(t => t.Id == typeId))) continue;
                if (entry.Value.IsVideo) throw new InvalidOperationException("فقط تصویر مجاز است.");
                var path = uploads.CopyToPermanent(entry.Value, false); paths.Add(path);
                var url = "/uploads/images/" + Path.GetFileName(path);
                if (slide is not null) slide.Image = url; else copy.TypePhotos[typeId] = url;
            }
            var row = await db.StorePageContents.FindAsync("homepage");
            if (row is null) { row = new() { Id = "homepage" }; db.StorePageContents.Add(row); }
            row.ContentJson = JsonSerializer.Serialize(copy); row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            Revision++;
            changes?.Notify();
            foreach (var upload in pending.Values) { try { uploads.Delete(upload); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Daily cleanup can remove the staging file. */ } }
            return null;
        }
        catch { foreach (var path in paths) File.Delete(path); throw; }
    }
    public async Task<string?> SaveIntervalAsync(ClaimsPrincipal? principal, int seconds)
    {
        if (seconds is < 3 or > 30) return "زمان نمایش باید بین ۳ تا ۳۰ ثانیه باشد.";
        await using var db = await factory.CreateDbContextAsync();
        var user = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await db.Users.SingleOrDefaultAsync(u => u.Id == id) : null;
        if (principal is null || user?.CanManageStore != true || !AccountAccess.Valid(principal, user, roles: true)) return "اجازه ویرایش ندارید.";
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(701127)");
        var row = await db.StorePageContents.FindAsync("homepage");
        var json = (row is null ? JsonSerializer.SerializeToNode(await ReadAsync()) : System.Text.Json.Nodes.JsonNode.Parse(row.ContentJson))?.AsObject();
        if (json is null) return "تنظیمات بنر قابل خواندن نیست.";
        json["IntervalSeconds"] = seconds;
        if (row is null) { row = new() { Id = "homepage" }; db.StorePageContents.Add(row); }
        row.ContentJson = json.ToJsonString(); row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Revision++;
        changes?.Notify();
        return null;
    }
    private static bool LocalUrl(string value) => value.Length <= 500 && value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\') && !value.Any(char.IsControl);
    private static bool ImageUrl(string value) => LocalUrl(value) && (value.StartsWith("/img/") || value.StartsWith("/uploads/"));
}
