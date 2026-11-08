using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Papey.Components;
using Papey.Data;
using Papey.Models;
using Papey.Services;
using Papey.Endpoints;
using Papey.Hubs;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// Load .env (project root) into process env, but never overwrite variables that are
// already set (e.g. by docker-compose env_file), so the container overrides local defaults.
DotNetEnv.Env.NoClobber().TraversePath().Load();
builder.Configuration.AddEnvironmentVariables();
if (builder.Configuration.GetValue<bool>("Deployment:RequireSecureConfiguration"))
{
    var adminPassword = builder.Configuration["ADMIN_PASSWORD"];
    if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 16 || adminPassword == "@Admin1234")
        throw new InvalidOperationException("Set a unique ADMIN_PASSWORD of at least 16 characters before deployment.");
}

// PostgreSQL via EF Core
var connStr = DatabaseConnection.Build(builder.Configuration);
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseNpgsql(connStr));

// Cookie auth
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.Events.OnValidatePrincipal = AccountAccess.ValidateCookie;
        if (builder.Configuration.GetValue<bool>("Deployment:HttpsOnly"))
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api/admin/printing") || context.Request.Path.StartsWithSegments("/hubs/admin/printing") || context.Request.Path.StartsWithSegments("/api/custom-designs"))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api/admin/printing") || context.Request.Path.StartsWithSegments("/hubs/admin/printing") || context.Request.Path.StartsWithSegments("/api/custom-designs"))
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddPapeyDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("custom-callbacks", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("custom-designs", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("printing-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
builder.Services.AddSignalR();
builder.Services.AddScoped<OrderPrintQueue>();
builder.Services.AddHostedService<PrintJobNotifier>();

// App services
builder.Services.AddDistributedMemoryCache();
builder.Services.Configure<StoreProfile>(builder.Configuration.GetSection("Store"));
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<DiscountService>();
builder.Services.AddScoped<StorefrontPricing>();
builder.Services.AddScoped<StoreSeo>();
builder.Services.AddScoped<ProductRecommendations>();
builder.Services.AddScoped<ProductReviewService>();
builder.Services.AddScoped<StaffAccounts>();
builder.Services.AddScoped<SizeGuideSettings>();
builder.Services.AddScoped<ProductTypeIconUploads>();
builder.Services.AddHostedService<ProductTypeIconCleanupWorker>();
builder.Services.AddScoped<CustomDesignService>();
builder.Services.AddScoped<CustomCallbackService>();
builder.Services.AddScoped<ProductTypePhotos>();
builder.Services.AddScoped<StorePageTexts>();
builder.Services.AddScoped<HomepageSettings>();
builder.Services.AddSingleton<HomepageChanges>();
builder.Services.AddScoped<CustomDesignSmsQueue>();
builder.Services.AddHostedService<CustomDesignSmsWorker>();
builder.Services.AddSingleton<TaxSettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ProductDraftUploads>();
builder.Services.AddHostedService<ProductDraftCleanupWorker>();
builder.Services.AddSingleton<OtpSignInTickets>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddHttpClient<SmsService>(http => http.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<SmsAudienceService>();
builder.Services.AddScoped<OrderAdminSmsQueue>();
builder.Services.AddHostedService<OrderAdminSmsWorker>();
builder.Services.AddScoped<ShippingSmsQueue>();
builder.Services.AddHostedService<ShippingSmsWorker>();
builder.Services.AddSingleton<ZarinpalService>();
builder.Services.AddHostedService<PendingOrderExpiryService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, StoreAuthenticationStateProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, AccountCircuitHandler>();

var app = builder.Build();

// Auto-migrate & seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    await DbSeeder.SeedAsync(db, app.Configuration);
    await DbSeeder.EnsureStoreSeededAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Only redirect to HTTPS when a forwarded scheme or local dev certificate is present.
// In Docker (plain HTTP on :8080) this would break WebSocket / Blazor Server circuits.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Render the storefront's 404 page for missing browser pages, preserving HTTP 404.
// Keep API and printer hub responses in their original format.
app.UseStatusCodePagesWithReExecute("/not-found");
app.Use(async (context, next) =>
{
    await next(context);
    if (context.Response.StatusCode != StatusCodes.Status404NotFound
        || context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/hubs")
        || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        var statusPages = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusPages is not null) statusPages.Enabled = false;
    }
});

// Public catalog URLs must describe their real HTTP status to crawlers.
app.Use(async (context, next) =>
{
    if(HttpMethods.IsGet(context.Request.Method)||HttpMethods.IsHead(context.Request.Method)) {
        var path=context.Request.Path.Value??"/";
        if(path=="/contact") { context.Response.Redirect("/about#contact",permanent:true); return; }
        if(path.Length>1 && path.EndsWith('/') && !path.StartsWith("/api/") && !path.StartsWith("/_")) {
            context.Response.Redirect(path.TrimEnd('/')+context.Request.QueryString,permanent:true);return;
        }
        var product=path.StartsWith("/product/",StringComparison.OrdinalIgnoreCase);
        var category=path.StartsWith("/category/",StringComparison.OrdinalIgnoreCase);
        if(product||category) {
            var factory=context.RequestServices.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            var slug=Uri.UnescapeDataString(path[(product?9:10)..]);
            var exists=product?await db.Products.AnyAsync(p=>p.Slug==slug&&p.IsActive,context.RequestAborted):await db.Categories.AnyAsync(c=>c.Slug==slug&&c.IsActive,context.RequestAborted);
            if(!exists) { context.Response.StatusCode=StatusCodes.Status404NotFound; return; }
        }
    }
    await next(context);
});

// Serve user-uploaded files (uploads, favicon, images) via classic static files.
app.Use(async (context, next) =>
{
    if (string.Equals(context.Request.Host.Host, "www.papey.ir", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("https://papey.ir" + context.Request.PathBase + context.Request.Path + context.Request.QueryString, permanent: true, preserveMethod: true);
        return;
    }
    await next(context);
});
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs")
        && !context.Request.Path.StartsWithSegments("/_blazor") && context.Request.Path != "/healthz")
        CartIdentity.Ensure(context, app.Configuration.GetValue<bool>("Deployment:HttpsOnly"));
    await next(context);
});

app.MapGet("/admin/product-drafts/{name}", (string name, HttpContext http, ProductDraftUploads uploads) =>
{
    http.Response.Headers.CacheControl = "no-store";
    try
    {
        var stream = uploads.OpenPreview(name);
        return stream is null ? Results.NotFound() : Results.Stream(stream, uploads.ContentType(name));
    }
    catch (ArgumentException) { return Results.NotFound(); }
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.UseMiddleware<PageViewMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapCustomDesignEndpoints();
app.MapGet("/sitemap.xml", async (StoreSeo seo) => Results.Text(await seo.SitemapAsync(), "application/xml; charset=utf-8"));
app.MapGet("/robots.txt", (StoreSeo seo) => Results.Text(
    "User-agent: *\nDisallow: /admin\nDisallow: /account\nDisallow: /cart\nDisallow: /checkout\nDisallow: /login\nDisallow: /register\nDisallow: /search\nDisallow: /payment/\nSitemap: " + seo.Url("/sitemap.xml") + "\n", "text/plain"));


// Cookie auth endpoints (login/register/logout via form posts)
app.MapPost("/account/login", async (HttpContext http, [FromForm] string identifier, [FromForm] string password,
    [FromQuery] string? returnUrl, AuthService auth) =>
{
    var user = await auth.ValidateLoginAsync(identifier, password);
    var failUrl = "/login?error=1" + (string.IsNullOrEmpty(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");
    if (user is null) return Results.Redirect(failUrl);
    await auth.SignInAsync(user);
    var target = LoginRedirect.ForUser(user, returnUrl);
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapPost("/account/register", async ([FromForm] string username, [FromForm] string password,
    [FromForm] string fullName, [FromForm] string phone, [FromForm] string? email,
    [FromQuery] string? returnUrl, AuthService auth) =>
{
    phone = SmsService.NormalizePhone(phone) ?? "";
    if (string.IsNullOrWhiteSpace(fullName) || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
        return Results.Redirect("/register/password?error=invalid");
    var (user, error) = await auth.RegisterAsync(username, password, email, phone, fullName);
    if (user is null) return Results.Redirect($"/register/password?error={error}");
    await auth.SignInAsync(user);
    var target = LoginRedirect.ForUser(user, returnUrl);
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapGet("/logout", async (AuthService auth) =>
{
    await auth.SignOutAsync();
    return Results.Redirect("/");
});

// Completes OTP sign-in inside a real HTTP request (cookies can only be set here)
app.MapGet("/account/otp-complete", async (string? token, string? returnUrl, AuthService auth, OtpSignInTickets tickets, HttpContext http) =>
{
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    var userId = tickets.Consume(token);
    if (userId is null) return Results.Redirect("/login?error=otp");
    var user = await auth.GetUserByIdAsync(userId.Value);
    if (user is null) return Results.Redirect("/login?error=otp");
    await auth.SignInAsync(user, otp: true);
    var target = LoginRedirect.ForUser(user, returnUrl);
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

// Design source images are private admin assets. They are deliberately stored
// outside wwwroot and can only be previewed or downloaded by an administrator.
app.MapGet("/admin/products/{productId:int}/designs.zip", async (int productId, HttpContext http, IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment env) =>
{
    if (!http.User.IsInRole("Admin")) return Results.Forbid();

    await using var db = await dbFactory.CreateDbContextAsync();
    var product = await db.Products.FindAsync(productId);
    if (product is null) return Results.NotFound();

    var designs = await db.ProductDesignMedia
        .Where(design => design.ProductId == productId)
        .OrderBy(design => design.DisplayOrder)
        .ToListAsync();
    if (designs.Count == 0) return Results.NotFound();

    await using var output = new MemoryStream();
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
        var fileIndex = 1;
        foreach (var design in designs)
        {
            var filePath = Path.Combine(env.ContentRootPath, "App_Data", "product-designs", Path.GetFileName(design.StoredFileName));
            if (!File.Exists(filePath)) continue;

            var entryName = $"{fileIndex:D2}-{Path.GetFileName(design.OriginalFileName)}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var input = File.OpenRead(filePath);
            await using var entryStream = entry.Open();
            await input.CopyToAsync(entryStream);
            fileIndex++;
        }
    }

    if (output.Length == 0) return Results.NotFound();
    return Results.File(output.ToArray(), "application/zip", $"{product.Slug}-designs.zip");
});

app.MapGet("/admin/products/{productId:int}/designs/{designId:int}/download", async (int productId, int designId, HttpContext http, IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment env) =>
{
    if (!http.User.IsInRole("Admin")) return Results.Forbid();

    await using var db = await dbFactory.CreateDbContextAsync();
    var design = await db.ProductDesignMedia.FirstOrDefaultAsync(item => item.Id == designId && item.ProductId == productId);
    if (design is null) return Results.NotFound();

    var filePath = Path.Combine(env.ContentRootPath, "App_Data", "product-designs", Path.GetFileName(design.StoredFileName));
    return File.Exists(filePath)
        ? Results.File(filePath, design.ContentType, design.OriginalFileName, enableRangeProcessing: true)
        : Results.NotFound();
});

app.MapGet("/admin/products/{productId:int}/designs/{designId:int}", async (int productId, int designId, HttpContext http, IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment env) =>
{
    if (!http.User.IsInRole("Admin")) return Results.Forbid();

    await using var db = await dbFactory.CreateDbContextAsync();
    var design = await db.ProductDesignMedia.FirstOrDefaultAsync(item => item.Id == designId && item.ProductId == productId);
    if (design is null) return Results.NotFound();

    var filePath = Path.Combine(env.ContentRootPath, "App_Data", "product-designs", Path.GetFileName(design.StoredFileName));
    return File.Exists(filePath)
        ? Results.File(filePath, design.ContentType, enableRangeProcessing: true)
        : Results.NotFound();
});

app.MapPrintingEndpoints();
app.MapHub<PrintingHub>("/hubs/admin/printing", options => options.CloseOnAuthenticationExpiration = true);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapStaticAssets();

app.Run();
