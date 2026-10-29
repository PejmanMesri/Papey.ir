namespace Papey.Services;

public static class CartIdentity
{
    public const string CookieName = "papey.cart";
    public const string ItemKey = "Papey.CartId";
    public static bool Valid(string? id) => Guid.TryParseExact(id, "N", out var value) && value != Guid.Empty;
    public static void Ensure(HttpContext context, bool secure)
    {
        var id = context.Request.Cookies[CookieName];
        if (!Valid(id))
        {
            id = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(CookieName, id, new CookieOptions { HttpOnly = true,
                Secure = secure || context.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true,
                Path = "/", MaxAge = TimeSpan.FromDays(7) });
        }
        context.Items[ItemKey] = id;
    }
}
