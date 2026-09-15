using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Papey.Components;
using Papey.Data;
using Papey.Services;

var builder = WebApplication.CreateBuilder(args);

// Load .env (project root) into process env, but never overwrite variables that are
// already set (e.g. by docker-compose env_file), so the container overrides local defaults.
DotNetEnv.Env.NoClobber().TraversePath().Load();
builder.Configuration.AddEnvironmentVariables();

// PostgreSQL via EF Core
var connStr =
    $"Host={builder.Configuration["DB_HOST"] ?? "localhost"};" +
    $"Port={builder.Configuration["DB_PORT"] ?? "5432"};" +
    $"Database={builder.Configuration["POSTGRES_DB"] ?? "papeydb"};" +
    $"Username={builder.Configuration["POSTGRES_USER"] ?? "papey"};" +
    $"Password={builder.Configuration["POSTGRES_PASSWORD"] ?? "papey123"}";
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseNpgsql(connStr));

// Cookie auth
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// App services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<SmsService>();
builder.Services.AddSingleton<ZarinpalService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Auto-migrate & seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    await DbSeeder.SeedAsync(db, app.Configuration);
    await DbSeeder.EnsureColorsSizesAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Cookie auth endpoints (login/register/logout via form posts)
app.MapPost("/account/login", async (HttpContext http, [FromForm] string identifier, [FromForm] string password,
    [FromQuery] string? returnUrl, AuthService auth) =>
{
    var user = await auth.ValidateLoginAsync(identifier, password);
    var failUrl = "/login?error=1" + (string.IsNullOrEmpty(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");
    if (user is null) return Results.Redirect(failUrl);
    await auth.SignInAsync(user);
    var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') ? returnUrl
        : user.IsAdmin ? "/admin" : "/";
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapPost("/account/register", async ([FromForm] string username, [FromForm] string password,
    [FromForm] string fullName, [FromForm] string phone, [FromForm] string? email,
    [FromQuery] string? returnUrl, AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(fullName) || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
        return Results.Redirect("/register?error=invalid");
    var (user, error) = await auth.RegisterAsync(username, password, email, phone, fullName);
    if (user is null) return Results.Redirect($"/register?error={error}");
    await auth.SignInAsync(user);
    var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') ? returnUrl : "/";
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapGet("/logout", async (AuthService auth) =>
{
    await auth.SignOutAsync();
    return Results.Redirect("/");
});

// Completes OTP sign-in inside a real HTTP request (cookies can only be set here)
app.MapGet("/account/otp-complete", async (int userId, string? returnUrl, AuthService auth) =>
{
    var user = await auth.GetUserByIdAsync(userId);
    if (user is null) return Results.Redirect("/login?error=otp");
    await auth.SignInAsync(user);
    var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') ? returnUrl
        : user.IsAdmin ? "/admin" : "/";
    return Results.Redirect(target);
});

// Media upload (admin only) — images & videos
app.MapPost("/admin/upload-media", async (HttpContext http, IWebHostEnvironment env) =>
{
    if (!http.User.IsInRole("Admin")) return Results.Forbid();
    var file = http.Request.Form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "no file" });

    var allowedImages = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg" };
    var allowedVideos = new[] { ".mp4", ".webm", ".mov" };
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var isVideo = allowedVideos.Contains(ext);
    if (!isVideo && !allowedImages.Contains(ext))
        return Results.BadRequest(new { error = "فرمت فایل مجاز نیست" });
    if (file.Length > 50 * 1024 * 1024)
        return Results.BadRequest(new { error = "حجم فایل بیش از حد مجاز است (حداکثر ۵۰ مگابایت)" });

    var folder = Path.Combine(env.WebRootPath, "uploads", isVideo ? "videos" : "images");
    Directory.CreateDirectory(folder);
    var name = $"{Guid.NewGuid():N}{ext}";
    await using (var fs = File.Create(Path.Combine(folder, name)))
        await file.CopyToAsync(fs);
    var url = $"/uploads/{(isVideo ? "videos" : "images")}/{name}";
    return Results.Ok(new { url, type = isVideo ? "video" : "image" });
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
