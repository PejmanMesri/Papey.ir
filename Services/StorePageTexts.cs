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
        new("eyebrow", "متن کوچک بالای عنوان", "درباره پاپی / کیفیت از انتخاب آغاز می‌شود", false, 300),
        new("heading", "عنوان اصلی", "معرفی پارچه‌ها و متریال\nمورد استفاده در مجموعه پوشاک پاپی", true, 300),
        new("body", "متن معرفی (هر بند در یک خط جدا)", "در مجموعه پوشاک پاپی کیفیت مواد اولیه، انتخاب پارچه مناسب و دوام محصولات از مهم‌ترین اولویت‌های ما در فرآیند تولید است. به همین دلیل، متناسب با نوع محصول، کاربرد و سفارش مشتری، از پارچه‌های باکیفیت و استاندارد استفاده می‌کنیم تا محصول نهایی از نظر زیبایی، راحتی، دوام و کیفیت دوخت در سطح مطلوبی قرار داشته باشد.", true, 10000),
        new("imageCaption", "نوشته روی تصویر", "WEAR\nYOUR STORY.", true, 300),
        new("shopButton", "متن دکمه فروشگاه", "کشف محصولات ←", false, 300),
        new("contactButton", "متن لینک تماس", "با ما در ارتباط باش ↗", false, 300),
        new("twoThreadTitle", "عنوان پارچه دورس دو نخ", "پارچه دورس دو نخ", false, 300),
        new("twoThreadBody", "توضیح پارچه دورس دو نخ", "پارچه دورس دو نخ یکی از پرکاربردترین پارچه‌ها در تولید پوشاک پاییزه و بهاره است. این پارچه با وزن و ضخامت متعادل، لطافت مناسب و انعطاف‌پذیری مطلوب، انتخابی مناسب برای تولید انواع دورس، هودی و سویشرت محسوب می‌شود. دورس دو نخ علاوه بر راحتی در استفاده روزمره، قابلیت حفظ فرم مناسبی داشته و برای تولید پوشاک اسپرت و کژوال گزینه‌ای کاربردی است.", true, 6000),
        new("threeThreadTitle", "عنوان پارچه دورس سه نخ", "پارچه دورس سه نخ", false, 300),
        new("threeThreadBody", "توضیح پارچه دورس سه نخ", "پارچه دورس سه نخ به دلیل ساختار ضخیم‌تر و وزن بیشتر نسبت به دورس دو نخ، گزینه‌ای مناسب برای تولید پوشاک گرم پاییزه و زمستانی است. این پارچه از قدرت حفظ گرمای بیشتری برخوردار بوده و برای تولید انواع هودی، دورس و سویشرت‌های ضخیم و گرم مورد استفاده قرار می‌گیرد. استحکام و ایستایی مناسب این پارچه، آن را به انتخابی مطلوب برای تولید پوشاک باکیفیت و بادوام تبدیل کرده است.", true, 6000),
        new("qualityTitle", "عنوان تعهد به کیفیت", "تعهد ما به کیفیت", false, 300),
        new("qualityBody", "متن تعهد به کیفیت", "در مجموعه‌ی پوشاک پاپی تلاش می‌کنیم با انتخاب متریال متناسب با نیاز هر سفارش، محصولاتی با کیفیت مطلوب، دوخت دقیق، تن‌خور مناسب و قیمت رقابتی تولید کنیم. هدف ما ارائه محصولی است که علاوه بر ظاهر زیبا، در استفاده مداوم نیز رضایت مشتریان را جلب کند.", true, 6000)
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
