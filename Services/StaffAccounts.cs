using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed record StaffAccount(int Id, string Username, string? Name, string? Phone, bool Disabled, DateTime CreatedAt);

public sealed class StaffAccounts(IDbContextFactory<AppDbContext> factory, AuthService auth)
{
    private async Task<bool> IsOwnerAsync(AppDbContext db)
    {
        var user = auth.CurrentUserId is { } id ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) : null;
        return user?.IsAdmin == true && AccountAccess.Valid(auth.CurrentPrincipal ?? new(), user);
    }
    public async Task<List<StaffAccount>> ReadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db)) throw new UnauthorizedAccessException();
        return await db.Users.AsNoTracking().Where(u => u.IsStaff && !u.IsAdmin).OrderByDescending(u => u.CreatedAt)
            .Select(u => new StaffAccount(u.Id, u.Username, u.FullName, u.Phone, u.IsDisabled, u.CreatedAt)).ToListAsync();
    }
    public async Task<string?> CreateAsync(string username, string name, string? phone, string password)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db)) return "فقط مدیر اصلی می‌تواند همکار تعریف کند.";
        username = username.Trim(); name = name.Trim();
        var normalizedPhone = SmsService.NormalizePhone(phone);
        if (!Regex.IsMatch(username, @"^[a-zA-Z0-9._-]{3,50}$") || name.Length is 0 or > 200)
            return "نام نمایشی و نام کاربری معتبر وارد کنید؛ نام کاربری ۳ تا ۵۰ حرف انگلیسی، عدد، نقطه، خط یا زیرخط.";
        if (password.Length is < 10 or > 128) return "رمز عبور باید ۱۰ تا ۱۲۸ کاراکتر باشد.";
        if (!string.IsNullOrWhiteSpace(phone) && normalizedPhone is null) return "شماره موبایل معتبر نیست.";
        if ((normalizedPhone is not null && auth.AdminPhones.Contains(normalizedPhone)) || auth.AdminPhones.Contains(SmsService.NormalizePhone(username) ?? ""))
            return "شماره مدیر اصلی قابل اختصاص به همکار نیست.";
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"staff-accounts"}, 0))");
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower())) return "این نام کاربری قبلاً استفاده شده است.";
        if (normalizedPhone is not null && await db.Users.AnyAsync(u => u.Phone == normalizedPhone)) return "این شماره موبایل قبلاً استفاده شده است.";
        db.Users.Add(new AppUser { Username = username, FullName = name, Phone = normalizedPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), IsStaff = true, IsAdmin = false });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return null;
    }
    public async Task<bool> SetDisabledAsync(int id, bool disabled)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db)) return false;
        return await db.Users.Where(u => u.Id == id && u.IsStaff && !u.IsAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDisabled, disabled).SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)) == 1;
    }
    public async Task<string?> ResetPasswordAsync(int id, string password)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db)) return "فقط مدیر اصلی مجاز به تغییر رمز همکاران است.";
        if (password.Length is < 10 or > 128) return "رمز عبور باید ۱۰ تا ۱۲۸ کاراکتر باشد.";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var changed = await db.Users.Where(u => u.Id == id && u.IsStaff && !u.IsAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1));
        return changed == 1 ? null : "حساب همکار یافت نشد.";
    }
}
