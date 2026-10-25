using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Claims;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
}

var clock = new TestClock();
var tickets = new OtpSignInTickets(clock);
Check(tickets.Consume(null) is null && tickets.Consume("1") is null, "Missing or user-ID-only proof is rejected");
var token = tickets.Create(42);
Check(tickets.Consume(new string('0', 64)) is null, "Guessed tickets are rejected");
Check(tickets.Consume(token) == 42 && tickets.Consume(token) is null, "Verified ticket is single-use");
var expired = tickets.Create(7);
clock.Now = clock.Now.AddMinutes(1);
Check(tickets.Consume(expired) is null, "Expired ticket is rejected at the expiry boundary");
var concurrent = tickets.Create(99);
var winners = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tickets.Consume(concurrent))));
Check(winners.Count(value => value == 99) == 1, "Concurrent redemption signs in at most once");

var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["DATABASE_URL"] = "postgresql://papey:p%40ss%3Bword%3Awith%2Fsymbols@database.internal/papeydb"
}).Build();
var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config));
Check(parsed.Host == "database.internal" && parsed.Port == 5432 && parsed.Database == "papeydb"
    && parsed.Username == "papey" && parsed.Password == "p@ss;word:with/symbols" && parsed.SslMode == SslMode.Require,
    "Managed database URL decodes credentials and requires TLS");
config["DATABASE_URL"] = null;
config["POSTGRES_PASSWORD"] = "secret;Host=attacker";
parsed = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config));
Check(parsed.Host == "localhost" && parsed.Password == "secret;Host=attacker", "Database password cannot inject connection settings");
config["ConnectionStrings:DefaultConnection"] = "Host=custom;Database=store;Username=user;Password=secret";
Check(new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config)).Host == "custom", "Explicit connection configuration takes precedence");

var protectionRoot = Path.Combine(Path.GetTempPath(), "papey-cookie-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(protectionRoot);
try
{
    var sharedKeys = Path.Combine(protectionRoot, "persistent-keys");
    ServiceProvider Release(string name, string? applicationName = null, bool legacy = false)
    {
        var release = Path.Combine(protectionRoot, name);
        Directory.CreateDirectory(release);
        var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = release });
        host.Configuration["DataProtection:KeyPath"] = sharedKeys;
        host.Configuration["DataProtection:ApplicationName"] = applicationName;
        if (legacy) host.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(sharedKeys));
        else host.Services.AddPapeyDataProtection(host.Configuration, host.Environment);
        host.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        return host.Services.BuildServiceProvider();
    }
    ISecureDataFormat<AuthenticationTicket> CookieFormat(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
    var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "42")], CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    using var releaseA = Release("release-a");
    var cookie = CookieFormat(releaseA).Protect(ticket);
    using var releaseB = Release("release-b");
    Check(CookieFormat(releaseB).Unprotect(cookie)?.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == "42",
        "An existing authentication cookie survives a different release directory");
    using var restarted = Release("release-b");
    Check(CookieFormat(restarted).Unprotect(cookie)?.Properties.IsPersistent == true,
        "A persistent login survives a new application service provider");
    using var otherApp = Release("other-app", "Unrelated.Storefront");
    Check(CookieFormat(otherApp).Unprotect(cookie) is null, "Different application identities remain isolated");
    using var legacy = Release("legacy-release", legacy: true);
    var legacyCookie = CookieFormat(legacy).Protect(ticket);
    var legacyIdentity = legacy.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
    using var upgraded = Release("upgraded-release", legacyIdentity);
    Check(CookieFormat(upgraded).Unprotect(legacyCookie)?.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == "42",
        "Retaining the previous discriminator preserves cookies issued before the fix");
}
finally
{
    Directory.Delete(protectionRoot, recursive: true);
}

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
