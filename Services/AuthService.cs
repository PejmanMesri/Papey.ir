using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public class AuthService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;
    private readonly SmsService _sms;
    private readonly IHostEnvironment _environment;

    public AuthService(IDbContextFactory<AppDbContext> dbFactory, IHttpContextAccessor http,
        IConfiguration config, SmsService sms, IHostEnvironment environment)
    {
        _dbFactory = dbFactory;
        _http = http;
        _config = config;
        _sms = sms;
        _environment = environment;
    }

    /// <summary>Verified phone aliases for the existing seeded admin account.</summary>
    public string[] AdminPhones => (_config["ADMIN_PHONES"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(SmsService.NormalizePhone).OfType<string>().Distinct().ToArray();

    private async Task<AppUser?> FindPhoneUserAsync(AppDbContext db, string phone)
    {
        if (AdminPhones.Contains(phone))
        {
            var username = (_config["ADMIN_USERNAME"] ?? "Admin").ToLowerInvariant();
            // A configured alias may only resolve to an existing administrator.
            return await db.Users.SingleOrDefaultAsync(u => u.IsAdmin && u.Username.ToLower() == username);
        }
        return await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    }

    /// <summary>Login by username OR phone number (username is case-insensitive).</summary>
    public async Task<AppUser?> ValidateLoginAsync(string identifier, string password)
    {
        identifier = identifier.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var phone = SmsService.NormalizePhone(identifier);
        var user = phone is not null && AdminPhones.Contains(phone)
            ? await FindPhoneUserAsync(db, phone)
            : await db.Users.FirstOrDefaultAsync(u =>
                u.Username.ToLower() == identifier.ToLower() || u.Phone == identifier);
        if (user is null) return null;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
    }

    /// <summary>Request an OTP. Returns the code (only non-null in dev so we can test).
    /// When requireExistingUser is set, only registered phones get a code (forgot-password flow).</summary>
    public async Task<(bool Success, string? DevCode, string? Error)> RequestOtpAsync(string phone, bool requireExistingUser = false)
    {
        phone = SmsService.NormalizePhone(phone) ?? "";
        if (phone.Length == 0)
            return (false, null, "شماره موبایل معتبر وارد کنید (09xxxxxxxxx)");

        if (!_sms.CanSendOtp)
            return (false, null, "ورود پیامکی در حال حاضر فعال نیست. از رمز عبور استفاده کنید.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (requireExistingUser && await FindPhoneUserAsync(db, phone) is null)
            return (false, null, "کاربری با این شماره موبایل یافت نشد.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({long.Parse(phone)})");
        var now = DateTime.UtcNow;
        if (await db.OtpCodes.AnyAsync(o => o.Phone == phone && o.ExpiresAt > now.AddMinutes(1)))
            return (false, null, "برای درخواست کد جدید، یک دقیقه صبر کنید.");
        if (await db.OtpCodes.CountAsync(o => o.Phone == phone && o.ExpiresAt > now.AddMinutes(-58)) >= 5)
            return (false, null, "تعداد درخواست‌ها بیش از حد مجاز است. بعداً دوباره تلاش کنید.");

        // Invalidate previous codes
        var old = await db.OtpCodes.Where(o => o.Phone == phone && !o.IsUsed).ToListAsync();
        foreach (var o in old) o.IsUsed = true;

        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000, 100000).ToString();
        db.OtpCodes.Add(new OtpCode
        {
            Phone = phone,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync();

        await transaction.CommitAsync();
        var sent = await _sms.SendOtpAsync(phone, code);
        if (!sent.Success)
        {
            var failedCode = await db.OtpCodes.FirstOrDefaultAsync(o => o.Phone == phone && o.Code == code && !o.IsUsed);
            if (failedCode is not null) failedCode.IsUsed = true;
            await db.SaveChangesAsync();
            return (false, null, sent.Error);
        }
        return (true, _environment.IsDevelopment() && _sms.IsSandbox ? code : null, null);
    }

    /// <summary>Verify OTP. Returns the matching user (created if needed). Sign-in is done separately via an HTTP endpoint.</summary>
    public async Task<AppUser?> VerifyOtpAndSignInAsync(string phone, string code)
    {
        phone = SmsService.NormalizePhone(phone) ?? ""; code = PersianHelpers.ToEnglishDigits(code.Trim());
        if (phone.Length == 0) return null;
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({long.Parse(phone)})");
        var otp = await db.OtpCodes
            .Where(o => o.Phone == phone && !o.IsUsed)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (otp is null || !otp.IsValid) return null;

        otp.Attempts++;
        if (otp.Code != code)
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }
        otp.IsUsed = true;

        var user = await FindPhoneUserAsync(db, phone);
        if (user is null && AdminPhones.Contains(phone))
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }
        if (user is null)
        {
            user = new AppUser
            {
                Username = phone,
                Phone = phone,
                FullName = "کاربر پاپی",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                IsAdmin = false
            };
            db.Users.Add(user);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return user;
    }

    public async Task<AppUser?> GetUserByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.FindAsync(id);
    }

    public async Task SignInAsync(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await _http.HttpContext!.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    public async Task SignOutAsync()
        => await _http.HttpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    public int? CurrentUserId =>
        int.TryParse(_http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public async Task<(AppUser? User, string? Error)> RegisterAsync(string username, string password, string? email, string? phone, string? fullName)
    {
        username = username.Trim();
        var normalizedPhone = SmsService.NormalizePhone(phone);
        var usernamePhone = SmsService.NormalizePhone(username);
        if ((normalizedPhone is not null && AdminPhones.Contains(normalizedPhone))
            || (usernamePhone is not null && AdminPhones.Contains(usernamePhone)))
            return (null, "reserved-phone");
        if (!string.IsNullOrWhiteSpace(phone) && normalizedPhone is null)
            return (null, "invalid");
        phone = normalizedPhone;
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            return (null, "duplicate");
        if (!string.IsNullOrWhiteSpace(phone) && await db.Users.AnyAsync(u => u.Phone == phone))
            return (null, "duplicate-phone");
        var user = new AppUser
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Email = email,
            Phone = phone,
            FullName = fullName,
            // Password registration does not prove ownership of an administrator's phone.
            IsAdmin = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, null);
    }

    /// <summary>Change password: requires the current password. Returns (Success, Error).</summary>
    public async Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        if (newPassword.Length < 6) return (false, "رمز عبور جدید باید حداقل ۶ کاراکتر باشد.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (false, "کاربر یافت نشد.");
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return (false, "رمز عبور فعلی اشتباه است.");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>Update display profile fields (username/phone are login identity — not editable here).</summary>
    public async Task<(bool Success, string? Error)> UpdateProfileAsync(int userId, string? fullName, string? email)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (false, "کاربر یافت نشد.");
        user.FullName = fullName?.Trim();
        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>Forgot-password: verify OTP then set a new password. Does not sign in.</summary>
    public async Task<(bool Success, string? Error)> ResetPasswordByOtpAsync(string phone, string code, string newPassword)
    {
        if (newPassword.Length < 6) return (false, "رمز عبور جدید باید حداقل ۶ کاراکتر باشد.");
        phone = SmsService.NormalizePhone(phone) ?? ""; code = PersianHelpers.ToEnglishDigits(code.Trim());
        if (phone.Length == 0) return (false, "شماره موبایل معتبر نیست.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({long.Parse(phone)})");
        var otp = await db.OtpCodes
            .Where(o => o.Phone == phone && !o.IsUsed)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (otp is null || !otp.IsValid) return (false, "کد وارد شده صحیح نیست یا منقضی شده است.");

        otp.Attempts++;
        if (otp.Code != code) { await db.SaveChangesAsync(); await transaction.CommitAsync(); return (false, "کد وارد شده صحیح نیست یا منقضی شده است."); }
        otp.IsUsed = true;

        var user = await FindPhoneUserAsync(db, phone);
        if (user is null) { await db.SaveChangesAsync(); await transaction.CommitAsync(); return (false, "کاربری با این شماره موبایل یافت نشد."); }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (true, null);
    }
}
