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

    /// <summary>Admin phone numbers from .env — users with these phones get Admin role.</summary>
    public string[] AdminPhones =>
        (_config["ADMIN_PHONES"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Login by username OR phone number (username is case-insensitive).</summary>
    public async Task<AppUser?> ValidateLoginAsync(string identifier, string password)
    {
        identifier = identifier.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == identifier.ToLower() || u.Phone == identifier);
        if (user is null) return null;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
    }

    /// <summary>Request an OTP. Returns the code (only non-null in dev so we can test).
    /// When requireExistingUser is set, only registered phones get a code (forgot-password flow).</summary>
    public async Task<(bool Success, string? DevCode, string? Error)> RequestOtpAsync(string phone, bool requireExistingUser = false)
    {
        phone = phone.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
            return (false, null, "شماره موبایل معتبر وارد کنید (09xxxxxxxxx)");

        var apiKey = _config["KAVENEGAR_API_KEY"] ?? "";
        var smsConfigured = !string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("YOUR_");
        if (!smsConfigured && !_environment.IsDevelopment())
            return (false, null, "ورود پیامکی در حال حاضر فعال نیست. از رمز عبور استفاده کنید.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (requireExistingUser && !await db.Users.AnyAsync(u => u.Phone == phone))
            return (false, null, "کاربری با این شماره موبایل یافت نشد.");

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

        if (!smsConfigured)
        {
            // No SMS provider configured — return code for local development/testing
            return (true, code, null);
        }

        await _sms.SendSmsAsync(phone, $"پاپی - کد ورود شما: {code}");
        return (true, null, null);
    }

    /// <summary>Verify OTP. Returns the matching user (created if needed). Sign-in is done separately via an HTTP endpoint.</summary>
    public async Task<AppUser?> VerifyOtpAndSignInAsync(string phone, string code)
    {
        phone = phone.Trim(); code = code.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var otp = await db.OtpCodes
            .Where(o => o.Phone == phone && !o.IsUsed)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (otp is null || !otp.IsValid) return null;

        otp.Attempts++;
        if (otp.Code != code)
        {
            await db.SaveChangesAsync();
            return null;
        }
        otp.IsUsed = true;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user is null)
        {
            user = new AppUser
            {
                Username = phone,
                Phone = phone,
                FullName = "کاربر پاپی",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                IsAdmin = AdminPhones.Contains(phone)
            };
            db.Users.Add(user);
        }
        else if (AdminPhones.Contains(phone) && !user.IsAdmin)
        {
            user.IsAdmin = true;
        }
        await db.SaveChangesAsync();
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
        phone = phone.Trim(); code = code.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var otp = await db.OtpCodes
            .Where(o => o.Phone == phone && !o.IsUsed)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();
        if (otp is null || !otp.IsValid) return (false, "کد وارد شده صحیح نیست یا منقضی شده است.");

        otp.Attempts++;
        if (otp.Code != code) { await db.SaveChangesAsync(); return (false, "کد وارد شده صحیح نیست یا منقضی شده است."); }
        otp.IsUsed = true;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user is null) { await db.SaveChangesAsync(); return (false, "کاربری با این شماره موبایل یافت نشد."); }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await db.SaveChangesAsync();
        return (true, null);
    }
}
