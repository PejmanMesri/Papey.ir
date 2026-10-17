using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

public sealed record SmsAudience(string[] Phones, int Accounts, int WithoutValidPhone);
public sealed record SmsBroadcastResult(int Accepted, int Rejected, int Unknown, int Unsent,
    string[] PackIds, string? Error)
{
    public bool Success => Rejected == 0 && Unknown == 0 && Unsent == 0 && Accepted > 0;
}

public sealed class SmsAudienceService(IDbContextFactory<AppDbContext> factory, SmsService sms)
{
    public async Task<SmsAudience> GetAllUsersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var phones = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => u.Phone).ToArrayAsync(ct);
        var normalized = phones.Select(SmsService.NormalizePhone).ToArray();
        return new(normalized.OfType<string>().Distinct().ToArray(), phones.Length,
            normalized.Count(p => p is null));
    }

    // The administrator reviews a fixed audience before this method is called.
    // Stop on any inconclusive/failing batch; never automatically repeat recipients.
    public async Task<SmsBroadcastResult> SendAsync(IEnumerable<string> phones, string message,
        CancellationToken ct = default)
    {
        var normalized = phones.Select(SmsService.NormalizePhone).ToArray();
        var recipients = normalized.OfType<string>().Distinct().ToArray();
        if (normalized.Any(p => p is null) || recipients.Length == 0 ||
            string.IsNullOrWhiteSpace(message) || message.Length > 1000 || !sms.CanSendBulk)
            return new(0, 0, 0, recipients.Length, [], "متن، گیرندگان یا تنظیمات ارسال معتبر نیست.");
        var accepted = 0;
        var rejected = 0;
        var attempted = 0;
        var packs = new List<string>();
        foreach (var batch in recipients.Chunk(100))
        {
            var result = await sms.SendBulkAsync(batch, message, ct);
            attempted += batch.Length;
            accepted += result.Accepted;
            rejected += result.Rejected;
            if (result.PackId is not null) packs.Add(result.PackId);
            if (!result.Success)
                return new(accepted, rejected,
                    result.Uncertain ? Math.Max(0, batch.Length - result.Accepted - result.Rejected) : 0,
                    recipients.Length - attempted, packs.ToArray(), result.Error);
        }
        return new(accepted, rejected, 0, 0, packs.ToArray(), null);
    }
}
