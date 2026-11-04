using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;
namespace Papey.Services;

public sealed record PageTextField(string Key, string Label, string Default, bool Multiline = false, int Limit = 300);

public sealed class StorePageTexts(IDbContextFactory<AppDbContext> factory)
{
    public static readonly PageTextField[] About = [
        new("eyebrow", "متن کوچک بالای عنوان", "درباره پاپی / OUR STORY"),
        new("heading", "عنوان اصلی", "لباس تو،\nروایت سلیقه تو.", true),
        new("body", "متن درباره ما (هر بند در یک خط جدا)", "پاپی برای علاقه‌هایی است که دوست داری با خودت همراه داشته باشی؛ از انیمه و بازی تا موسیقی، هنر و داستان‌هایی که برایت معنا دارند.\nما تیشرت، هودی و لانگ اسلیو را به بومی برای این علاقه‌ها تبدیل می‌کنیم. مدل لباس، طرح، رنگ و سایز را انتخاب کن و استایل خودت را بساز.\nاگر طرحی در ذهن داری که هنوز در فروشگاه نیست، برای سفارش اختصاصی با ما صحبت کن. جزئیات و قیمت پیش از ثبت نهایی با تو هماهنگ می‌شود.", true, 10000),
        new("imageCaption", "نوشته روی تصویر", "WEAR\nYOUR STORY.", true),
        new("shopButton", "متن دکمه فروشگاه", "کشف محصولات ←"),
        new("contactButton", "متن لینک تماس", "با ما در ارتباط باش ↗")
    ];
    public static readonly PageTextField[] Contact = [
        new("eyebrow", "متن کوچک بالای عنوان", "تماس با ما / LET’S TALK"),
        new("heading", "عنوان اصلی", "یک گفت‌وگو تا\nانتخاب بعدی تو.", true),
        new("intro", "توضیحات صفحه", "برای راهنمای خرید، پیگیری سفارش یا صحبت درباره طرح اختصاصی، از این راه‌ها با پاپی در ارتباط باش.", true, 3000),
        new("whatsappLabel", "برچسب واتساپ", "۰۱ / WHATSAPP"),
        new("whatsappTitle", "عنوان واتساپ", "در واتساپ پیام بده"),
        new("whatsappBody", "توضیحات واتساپ", "عکس طرح یا پرسشت را برایمان بفرست.", true, 2000),
        new("whatsappButton", "متن دکمه واتساپ", "شروع گفت‌وگو ↗"),
        new("phoneLabel", "برچسب تلفن", "۰۲ / PHONE"),
        new("phoneTitle", "عنوان تلفن", "با پاپی تماس بگیر"),
        new("phoneBody", "ساعت پاسخگویی / توضیحات تلفن", "شنبه تا پنجشنبه، ۹ صبح تا ۹ شب", true, 2000),
        new("instagramLabel", "برچسب اینستاگرام", "۰۳ / INSTAGRAM"),
        new("instagramTitle", "عنوان اینستاگرام", "پاپی در اینستاگرام"),
        new("instagramBody", "توضیحات اینستاگرام", "طرح‌ها و تازه‌های فروشگاه را ببین.", true, 2000),
        new("customTitle", "عنوان بخش سفارش اختصاصی", "لباس با طرح خودت را می‌خواهی؟"),
        new("customBody", "توضیحات سفارش اختصاصی", "شماره‌ات را در صفحه سفارش اختصاصی ثبت کن تا با تو تماس بگیریم.", true, 2000),
        new("customButton", "متن دکمه سفارش اختصاصی", "درخواست تماس ←")
    ];
    public static PageTextField[] Fields(string page) => page switch { "about" => About, "contact" => Contact, _ => throw new ArgumentException("صفحه معتبر نیست.") };
    public async Task<Dictionary<string, string>> ReadAsync(string page)
    {
        var fields = Fields(page);
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.StorePageContents.AsNoTracking().SingleOrDefaultAsync(p => p.Id == page);
        var saved = row is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.ContentJson);
        return fields.ToDictionary(f => f.Key, f => saved?.GetValueOrDefault(f.Key) ?? f.Default);
    }
    public async Task<string?> SaveAsync(ClaimsPrincipal? principal, string page, IReadOnlyDictionary<string, string> values)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id) : null;
        if (principal is null || user?.CanManageStore != true || !AccountAccess.Valid(principal, user, roles: true)) return "اجازه ویرایش صفحه را ندارید.";
        var fields = Fields(page);
        var clean = new Dictionary<string, string>();
        foreach (var field in fields)
        {
            var value = values.GetValueOrDefault(field.Key)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value) || value.Length > field.Limit) return $"«{field.Label}» را وارد کنید؛ حداکثر {field.Limit} کاراکتر.";
            clean[field.Key] = value;
        }
        var row = await db.StorePageContents.FindAsync(page);
        if (row is null) { row = new() { Id = page }; db.StorePageContents.Add(row); }
        row.ContentJson = JsonSerializer.Serialize(clean); row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); return null;
    }
}
