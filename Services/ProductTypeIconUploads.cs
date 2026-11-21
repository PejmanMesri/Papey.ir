using System.Security.Claims;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

public sealed class ProductTypeIconUploads(IWebHostEnvironment env, IDbContextFactory<AppDbContext> factory)
{
    public const long MaxBytes = 2 * 1024 * 1024;
    private string Folder => Path.Combine(env.WebRootPath, "uploads", "type-icons");
    public static bool IsUploaded(string? value) => value is not null &&
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^/uploads/type-icons/[a-f0-9]{32}\.(png|jpg|jpeg|webp|gif|avif|ico|bmp|svg)$");

    public async Task<string> SaveAsync(ClaimsPrincipal? principal, Stream input, string name)
    {
        if (principal is null) throw new InvalidOperationException("اجازه بارگذاری آیکن ندارید.");
        await using var db = await factory.CreateDbContextAsync();
        var user = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id) : null;
        if (user?.CanManageStore != true || !AccountAccess.Valid(principal, user, roles: true))
            throw new InvalidOperationException("اجازه بارگذاری آیکن ندارید.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidOperationException("حداکثر حجم آیکن ۲ مگابایت است.");
            buffer.Write(chunk, 0, read);
        }
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var bytes = Validate(buffer.ToArray(), extension);
        Directory.CreateDirectory(Folder);
        var file = Guid.NewGuid().ToString("N") + extension;
        await File.WriteAllBytesAsync(Path.Combine(Folder, file), bytes);
        return "/uploads/type-icons/" + file;
    }

    public static byte[] Validate(byte[] bytes, string extension, long maxBytes = MaxBytes)
    {
        if (bytes.Length == 0 || bytes.Length > maxBytes) throw new InvalidOperationException($"فایل خالی یا بیش از {maxBytes / (1024 * 1024)} مگابایت است.");
        bool Starts(params byte[] prefix) => bytes.AsSpan().StartsWith(prefix);
        var valid = extension switch
        {
            ".png" => Starts(137, 80, 78, 71, 13, 10, 26, 10),
            ".jpg" or ".jpeg" => Starts(255, 216, 255),
            ".gif" => Starts("GIF87a"u8.ToArray()) || Starts("GIF89a"u8.ToArray()),
            ".webp" => bytes.Length >= 12 && Starts("RIFF"u8.ToArray()) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".avif" => bytes.Length >= 16 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
                (bytes.AsSpan(8, 4).SequenceEqual("avif"u8) || bytes.AsSpan(8, 4).SequenceEqual("avis"u8)),
            ".ico" => Starts(0, 0, 1, 0),
            ".bmp" => Starts(66, 77),
            ".svg" => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("فرمت یا محتوای تصویر معتبر نیست.");
        if (extension != ".svg") return bytes;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var doc = XDocument.Load(reader);
            XNamespace svg = "http://www.w3.org/2000/svg";
            if (doc.Root?.Name != svg + "svg") throw new XmlException();
            var tags = new HashSet<string>("svg g path rect circle ellipse line polyline polygon defs linearGradient radialGradient stop title desc".Split(' '));
            var attributes = new HashSet<string>("viewBox width height x y x1 x2 y1 y2 cx cy r rx ry d points fill stroke stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset fill-rule clip-rule opacity fill-opacity stroke-opacity transform id offset stop-color stop-opacity gradientUnits gradientTransform spreadMethod fx fy fr version".Split(' '));
            foreach (var element in doc.Root.DescendantsAndSelf())
            {
                if (element.Name.Namespace != svg || !tags.Contains(element.Name.LocalName)) throw new XmlException();
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration) continue;
                    if (attribute.Name.Namespace != XNamespace.None || !attributes.Contains(attribute.Name.LocalName)) throw new XmlException();
                    var value = attribute.Value;
                    if (value.Contains("url", StringComparison.OrdinalIgnoreCase) &&
                        !System.Text.RegularExpressions.Regex.IsMatch(value, @"^url\(#[a-zA-Z0-9_-]+\)$")) throw new XmlException();
                    if (value.Contains(':') || value.Contains('\\')) throw new XmlException();
                }
            }
            return System.Text.Encoding.UTF8.GetBytes(doc.Root.ToString(SaveOptions.DisableFormatting));
        }
        catch (XmlException) { throw new InvalidOperationException("SVG باید ساده و بدون اسکریپت، استایل، متن یا لینک خارجی باشد؛ از PNG استفاده کنید."); }
    }

    public async Task CleanupAsync()
    {
        if (!Directory.Exists(Folder)) return;
        await using var db = await factory.CreateDbContextAsync();
        var used = (await db.ProductTypes.Select(t => t.FilterIcon).ToListAsync()).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(Folder))
        {
            if (File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-24) || used.Contains("/uploads/type-icons/" + Path.GetFileName(path))) continue;
            File.Delete(path);
        }
    }
}

public sealed class ProductTypeIconCleanupWorker(IServiceScopeFactory scopes, ILogger<ProductTypeIconCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ProductTypeIconUploads>().CleanupAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Product type icon cleanup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
