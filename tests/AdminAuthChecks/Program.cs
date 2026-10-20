using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var connection = DatabaseConnection.Build(new ConfigurationBuilder().AddEnvironmentVariables().Build());
if (!new NpgsqlConnectionStringBuilder(connection).Database!.StartsWith("papey_admin_review_"))
    throw new InvalidOperationException("Use a new disposable papey_admin_review_* database.");
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
var factory = new Factory(options);
await using var db = factory.CreateDbContext();
await db.Database.MigrateAsync();
string[] phones = ["09122197962", "09124050885", "09022121464"];
const string password = "AuthReview!20261020";
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ADMIN_PHONES"] = string.Join(',', phones), ["ADMIN_USERNAME"] = "Admin" }).Build();
var environment = new TestEnvironment();
var sms = new SmsService(new HttpClient(new RejectNetwork()), config, environment, NullLogger<SmsService>.Instance);
var auth = new AuthService(factory, new HttpContextAccessor(), config, sms, environment);
var admin = new AppUser { Username = "Admin", IsAdmin = true, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
// A legacy non-admin record must never shadow the configured admin alias.
db.Users.AddRange(admin, new AppUser { Username = "Legacy", Phone = phones[1], PasswordHash = BCrypt.Net.BCrypt.HashPassword("WrongCustomerPassword") });
await db.SaveChangesAsync();
int checks = 0;
void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks++; Console.WriteLine("PASS: " + description); }
IEnumerable<string> Formats(string phone) => [phone, PersianHelpers.ToPersianDigits(phone), "+98" + phone[1..], "0098" + phone[1..], "98" + phone[1..], phone[1..]];
async Task AddOtp(string phone)
{
    db.OtpCodes.Add(new OtpCode { Phone = phone, Code = "54321", ExpiresAt = DateTime.UtcNow.AddMinutes(2) });
    await db.SaveChangesAsync();
}
foreach (var phone in phones)
{
    foreach (var format in Formats(phone))
    {
        var registered = await auth.RegisterAsync("NewCustomer", password, null, format, "Test");
        Check(registered.User is null && registered.Error == "reserved-phone", "Admin phone format is reserved against password signup");
        registered = await auth.RegisterAsync(format, password, null, "09120000000", "Test");
        Check(registered.User is null && registered.Error == "reserved-phone", "Admin phone cannot be used as a new username");
    }
    Check((await auth.ValidateLoginAsync("+98" + phone[1..], password))?.Id == admin.Id, "Password login by admin alias resolves the existing administrator");
    await AddOtp(phone);
    var loggedIn = await auth.VerifyOtpAndSignInAsync(PersianHelpers.ToPersianDigits(phone), "۵۴۳۲۱");
    Check(loggedIn?.Id == admin.Id && loggedIn.IsAdmin, "Verified admin phone signs into the existing admin account");
    Check(await auth.VerifyOtpAndSignInAsync(phone, "54321") is null, "Admin OTP cannot be replayed");
}
Check(await db.Users.CountAsync() == 2, "Admin OTP and signup attempts created no users");
Check(await auth.ValidateLoginAsync(phones[1], "WrongCustomerPassword") is null, "Legacy customer password cannot authenticate an admin alias");
Check(new[] { (string?)null, "/", "/checkout", "https://example.com" }.All(url => LoginRedirect.ForUser(admin, url) == "/admin"), "Admin sign-in always redirects to /admin, including a checkout return URL");
var customer = await auth.RegisterAsync("Customer", password, null, "+989120000000", "Customer");
Check(customer.User is { IsAdmin: false, Phone: "09120000000" }, "Normal customer registration still works and normalizes the phone");
Check(LoginRedirect.ForUser(customer.User!, "/checkout") == "/checkout", "Customer checkout return URL remains supported");
Check(new[] { "//example.com", "/\\example.com", "https://example.com", "/\r\nLocation:bad" }.All(url => LoginRedirect.ForUser(customer.User!, url) == "/"), "Invalid or external sign-in redirects fall back to home");
admin.IsAdmin = false; await db.SaveChangesAsync();
var countBefore = await db.Users.CountAsync();
await AddOtp(phones[0]);
Check(await auth.VerifyOtpAndSignInAsync(phones[0], "54321") is null && await db.Users.CountAsync() == countBefore, "A missing administrator never causes alias signup or privilege promotion");
admin.IsAdmin = true; await db.SaveChangesAsync();
await AddOtp("09120000001");
var normalOtp = await auth.VerifyOtpAndSignInAsync("09120000001", "54321");
Check(normalOtp is { IsAdmin: false, Phone: "09120000001" }, "An ordinary verified phone can still create a customer account");
Console.WriteLine($"{checks} admin authentication checks passed; no SMS sent.");

sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{ public AppDbContext CreateDbContext() => new(options); }
sealed class RejectNetwork : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("No SMS network calls allowed in auth tests."); }
sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "AuthChecks";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
