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

    public AuthService(IDbContextFactory<AppDbContext> dbFactory, IHttpContextAccessor http,
        IConfiguration config, SmsService sms)
    {
        _dbFactory = dbFactory;
        _http = http;
        _config = config;
        _sms = sms;
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

    /// <summary>Request an OTP for phone login. Returns the code (only non-null in dev so we can test).</summary>
    public async Task<(bool Success, string? DevCode, string? Error)> RequestOtpAsync(string phone)
    {
        phone = phone.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
            return (false, null, "شماره موبایل معتبر وارد کنید (09xxxxxxxxx)");

        await using var db = await _dbFactory.CreateDbContextAsync();
        // Invalidate previous codes
        var old = await db.OtpCodes.Where(o => o.Phone == phone && !o.IsUsed).ToListAsync();
        foreach (var o in old) o.IsUsed = true;

        var code = Random.Shared.Next(10000, 99999).ToString();
        db.OtpCodes.Add(new OtpCode
        {
            Phone = phone,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync();

        var apiKey = _config["KAVENEGAR_API_KEY"] ?? "";
        if (apiKey.StartsWith("YOUR_") || string.IsNullOrWhiteSpace(apiKey))
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
            IsAdmin = !string.IsNullOrWhiteSpace(phone) && AdminPhones.Contains(phone)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, null);
    }
}
