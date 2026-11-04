using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;
namespace Papey.Services;

public sealed class CustomCallbackService(IDbContextFactory<AppDbContext> factory, SmsService sms)
{
    public async Task SubmitAsync(string? input, CancellationToken ct)
    {
        if ((input?.Length ?? 0) > 30 || SmsService.NormalizePhone(input) is not string phone)
            throw new ArgumentException("شماره موبایل معتبر وارد کنید؛ مثلاً ۰۹۱۲۱۲۳۴۵۶۷.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize repeated submissions for this normalized phone, including parallel requests.
        var lockKey = long.Parse(phone, System.Globalization.CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        if (await db.CustomCallbackRequests.AnyAsync(r => r.Phone == phone && r.CreatedAt > DateTime.UtcNow.AddHours(-24), ct)) return;
        var request = new CustomCallbackRequest { Phone = phone };
        foreach (var recipient in sms.OrderNotificationPhones)
            request.SmsJobs.Add(new() { Phone = recipient, IsSandbox = sms.IsSandbox });
        db.CustomCallbackRequests.Add(request);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
