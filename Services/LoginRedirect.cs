using Papey.Models;

namespace Papey.Services;

public static class LoginRedirect
{
    public static string ForUser(AppUser user, string? returnUrl)
    {
        // Admin sign-in always opens the dashboard, including OTP handoffs that pass "/".
        if (user.CanManageStore) return "/admin";
        return !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl)
                ? returnUrl : "/";
    }
}
