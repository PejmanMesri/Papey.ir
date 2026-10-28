using System.Buffers.Binary;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed class CustomDesignService(IDbContextFactory<AppDbContext> factory, IWebHostEnvironment environment, SmsService sms)
{
    public const long MaxFileBytes = 5 * 1024 * 1024, MaxTotalBytes = 24 * 1024 * 1024;
    public string Folder(Guid id) => Path.Combine(environment.ContentRootPath, "App_Data", "custom-designs", id.ToString("N"));
    public async Task<bool> UpdateAsync(ClaimsPrincipal principal, Guid id, CustomDesignStatus status, string reply, string notes, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status) || reply.Length > 2000 || notes.Length > 2000) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var uid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == uid, ct);
        if (user?.CanManageStore != true || !AccountAccess.Valid(principal, user, roles: true)) return false;
        var request = await db.CustomDesignRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return false;
        reply = reply.Trim();
        if (request.AdminReply != reply) request.AdminRepliedAt = reply.Length > 0 ? DateTime.UtcNow : null;
        request.Status = status; request.AdminReply = reply; request.AdminNotes = notes.Trim();
        await db.SaveChangesAsync(ct); return true;
    }
    public static bool ValidLayers(IReadOnlyList<DesignLayer>? layers, int count) => layers is { Count: > 0 and <= 12 }
        && count is > 0 and <= 8 && Enumerable.Range(0, count).All(i => layers.Any(l => l.File == i))
        && layers.All(l => l.File >= 0 && l.File < count && l.Side is "front" or "back" && l.CanvasVersion is 1 or 2
            && double.IsFinite(l.X) && double.IsFinite(l.Y) && double.IsFinite(l.Width) && double.IsFinite(l.Height)
            && double.IsFinite(l.Rotation) && l.Rotation is >= -180 and <= 180 && (l.CanvasVersion == 2 || l.Rotation == 0)
            && l.Width is >= .02 and <= 1 && l.Height is >= .02 and <= 1
            && l.X >= 0 && l.Y >= 0 && l.X + l.Width <= 1.00001 && l.Y + l.Height <= 1.00001 && WithinCanvas(l));

    private static bool WithinCanvas(DesignLayer l)
    {
        if (l.CanvasVersion == 1) return true;
        var angle = l.Rotation * Math.PI / 180;
        var halfX = (Math.Abs(Math.Cos(angle)) * l.Width + Math.Abs(Math.Sin(angle)) * l.Height / .8) / 2;
        var halfY = (Math.Abs(Math.Sin(angle)) * l.Width * .8 + Math.Abs(Math.Cos(angle)) * l.Height) / 2;
        var cx = l.X + l.Width / 2; var cy = l.Y + l.Height / 2;
        return cx - halfX >= -.00001 && cy - halfY >= -.00001 && cx + halfX <= 1.00001 && cy + halfY <= 1.00001;
    }

    // Parse the PNG envelope before storing: dimensions, chunk boundaries, CRCs and terminal IEND.
    // Files stay private and are only served with image/png and nosniff.
    public static bool ValidPng(byte[] bytes)
    {
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return false;
        var offset = 8; var first = true; var data = false; var chunks = 0;
        while (offset <= bytes.Length - 12 && ++chunks <= 10000)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) return false;
            var size = (int)length;
            var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (first)
            {
                if (type != "IHDR" || size != 13) return false;
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4));
                if (width is 0 or > 12000 || height is 0 or > 12000 || (long)width * height > 40_000_000) return false;
                first = false;
            }
            else if (type == "IHDR") return false;
            uint crc = 0xffffffff;
            foreach (var b in bytes.AsSpan(offset + 4, size + 4))
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + size, 4))) return false;
            if (type == "IDAT") data = true;
            offset += size + 12;
            if (type == "IEND") return size == 0 && data && offset == bytes.Length;
        }
        return false;
    }

    public async Task<CustomDesignRequest> SubmitAsync(ClaimsPrincipal principal, IFormCollection form, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var uid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == uid, ct);
        if (!AccountAccess.Valid(principal, user)) throw new UnauthorizedAccessException();
        if (!Guid.TryParse(form["key"], out var key) || key == Guid.Empty) throw new ArgumentException("درخواست معتبر نیست؛ صفحه را تازه کنید.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize per customer: retries are idempotent and parallel requests cannot bypass limits.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({84762}, {uid})", ct);
        var previous = await db.CustomDesignRequests.SingleOrDefaultAsync(r => r.UserId == uid && r.SubmissionKey == key, ct);
        if (previous is not null) return previous;
        if (await db.CustomDesignRequests.CountAsync(r => r.UserId == uid && r.CreatedAt > DateTime.UtcNow.AddDays(-1), ct) >= 5)
            throw new ArgumentException("حداکثر ۵ درخواست در روز قابل ثبت است. برای پیگیری با پاپی تماس بگیرید.");
        var name = form["name"].ToString().Trim(); var phone = SmsService.NormalizePhone(form["phone"]);
        var garment = form["garment"].ToString(); var colour = form["colour"].ToString(); var size = form["size"].ToString();
        var notes = form["notes"].ToString().Trim();
        if (name.Length is < 2 or > 100 || phone is null || !CustomDesignCatalog.Valid(garment, colour, size)
            || !int.TryParse(form["quantity"], out var quantity) || quantity is < 1 or > 100 || notes.Length > 2000)
            throw new ArgumentException("نام، شماره تماس، مدل، سایز و تعداد را بررسی کنید.");
        if (form["layers"].ToString().Length > 12000) throw new ArgumentException("تعداد طرح‌ها بیش از حد مجاز است.");
        List<DesignLayer>? layers;
        try { layers = JsonSerializer.Deserialize<List<DesignLayer>>(form["layers"].ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { throw new ArgumentException("چیدمان طرح معتبر نیست."); }
        var files = form.Files;
        if (!ValidLayers(layers, files.Count) || files.Any(f => f.Length is <= 0 or > MaxFileBytes) || files.Sum(f => f.Length) > MaxTotalBytes)
            throw new ArgumentException("حداکثر ۸ فایل PNG، هر فایل ۵ مگابایت و مجموعاً ۲۴ مگابایت مجاز است.");
        var request = new CustomDesignRequest { SubmissionKey = key, UserId = uid, Name = name, Phone = phone,
            Garment = garment, Colour = colour, Size = size, Quantity = quantity, Notes = notes, LayersJson = JsonSerializer.Serialize(layers) };
        var folder = Folder(request.Id); Directory.CreateDirectory(folder);
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                await using var stream = files[i].OpenReadStream(); using var output = new MemoryStream();
                await stream.CopyToAsync(output, ct); var bytes = output.ToArray();
                if (bytes.Length > MaxFileBytes || !ValidPng(bytes)) throw new ArgumentException("فقط فایل PNG سالم با ابعاد حداکثر ۱۲۰۰۰ پیکسل و ۴۰ مگاپیکسل قابل ارسال است.");
                await File.WriteAllBytesAsync(Path.Combine(folder, $"{i}.png"), bytes, ct);
            }
            foreach (var recipient in sms.OrderNotificationPhones)
                request.SmsJobs.Add(new() { Phone = recipient, IsSandbox = sms.IsSandbox });
            db.CustomDesignRequests.Add(request); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return request;
        }
        catch { Directory.Delete(folder, true); throw; }
    }

    public async Task<bool> MayReadAsync(ClaimsPrincipal principal, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var uid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == uid, ct);
        return AccountAccess.Valid(principal, user, roles: true)
            && await db.CustomDesignRequests.AnyAsync(r => r.Id == id && (r.UserId == uid || user!.CanManageStore), ct);
    }
}
