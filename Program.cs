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
app.MapPost("/account/login", async (HttpContext http, [FromForm] string username, [FromForm] string password, AuthService auth) =>
{
    var user = await auth.ValidateLoginAsync(username, password);
    if (user is null) return Results.Redirect("/login?error=1");
    await auth.SignInAsync(user);
    return Results.Redirect(user.IsAdmin ? "/admin" : "/");
}).DisableAntiforgery();

app.MapPost("/account/register", async ([FromForm] string username, [FromForm] string password,
    [FromForm] string? fullName, [FromForm] string? phone, [FromForm] string? email, AuthService auth) =>
{
    var user = await auth.RegisterAsync(username, password, email, phone, fullName);
    if (user is null) return Results.Redirect("/register?error=duplicate");
    await auth.SignInAsync(user);
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapGet("/logout", async (AuthService auth) =>
{
    await auth.SignOutAsync();
    return Results.Redirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
