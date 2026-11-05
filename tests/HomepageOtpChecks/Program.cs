using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

var checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? throw new Exception("Disposable database required")) { Pooling = false };
var database = "papey_home_otp_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await admin.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync(); source.Database = database;
var factory = new Factory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
var folder = Path.Combine(Path.GetTempPath(), "papey-home-otp-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
    var environment = new TestEnvironment { ContentRootPath = folder, WebRootPath = Path.Combine(folder, "wwwroot") };
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["SMSIR_SANDBOX_API_KEY"] = "fake-for-intercepted-tests" }).Build();
    var handler = new FakeSms();
    var sms = new SmsService(new HttpClient(handler), config, environment, NullLogger<SmsService>.Instance);
    AuthService Auth() => new(factory, new HttpContextAccessor(), config, sms, environment);
    const string phone = "09120000001";
    Check((await Auth().RequestOtpAsync(phone)).Success && handler.Calls == 1, "First OTP request sends exactly one intercepted message");
    var code = await db.OtpCodes.SingleAsync(); code.ExpiresAt = DateTime.UtcNow.AddSeconds(25); code.IsUsed = true; await db.SaveChangesAsync();
    Check(!(await Auth().RequestOtpAsync("+989120000001")).Success && handler.Calls == 1, "Normalized phone and used OTP cannot bypass the second minute of cooldown");
    Check(await Auth().OtpCooldownSecondsAsync(phone) is > 0 and <= 25, "Server reports remaining seconds across new sessions");
    db.Users.Add(new() { Username = "buyer", Phone = phone, PasswordHash = "unused" }); await db.SaveChangesAsync();
    Check(!(await Auth().RequestOtpAsync(phone, true)).Success && handler.Calls == 1, "Password recovery shares the login cooldown");
    code.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
    Check((await Auth().RequestOtpAsync(phone)).Success && handler.Calls == 2, "OTP may be resent after the complete two-minute period");
    var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Auth().RequestOtpAsync("09120000002")));
    Check(concurrent.Count(r => r.Success) == 1 && handler.Calls == 3, "Concurrent sessions cannot send duplicate SMS messages");
    handler.Fail = true;
    Check(!(await Auth().RequestOtpAsync("09120000003")).Success, "Provider rejection returns a failure");
    var calls = handler.Calls;
    Check(!(await Auth().RequestOtpAsync("09120000003")).Success && handler.Calls == calls, "Failed deliveries still enforce the cooldown");

    var owner = new AppUser { Username = "owner", IsAdmin = true, PasswordHash = "unused" };
    var staff = new AppUser { Username = "staff", IsStaff = true, PasswordHash = "unused" };
    var customer = new AppUser { Username = "customer", PasswordHash = "unused" };
    var type = new ProductTypeEntity { Name = "هودی" }; db.Users.AddRange(owner, staff, customer); db.ProductTypes.Add(type); await db.SaveChangesAsync();
    var uploads = new ProductDraftUploads(environment, TimeProvider.System); var settings = new HomepageSettings(factory, uploads);
    var content = new HomepageContent { Slides = [new() { Title = "First", Image = "/img/custom-studio/tshirt-black-front.webp" }, new() { Title = "Second", Active = false, Image = "/img/custom-studio/hoodie-black-front.webp" }] };
    Check(await settings.SaveAsync(AccountAccess.Principal(customer), content, new Dictionary<string,ProductDraftUploads.Upload>()) is not null, "Customers cannot change banners");
    Check(await settings.SaveAsync(AccountAccess.Principal(owner), content, new Dictionary<string,ProductDraftUploads.Upload>()) is null, "Owner can create and save banners");
    content.Slides.Reverse(); content.IntervalSeconds = 8;
    Check(await settings.SaveAsync(AccountAccess.Principal(staff), content, new Dictionary<string,ProductDraftUploads.Upload>()) is null, "Active seller can manage homepage content");
    var saved = await settings.ReadAsync();
    Check(saved.Slides[0].Title == "Second" && !saved.Slides[0].Active && saved.IntervalSeconds == 8, "Slide order, visibility and timing persist");
    content.Slides[0].Link = "//evil.example";
    Check(await settings.SaveAsync(AccountAccess.Principal(owner), content, new Dictionary<string,ProductDraftUploads.Upload>()) is not null, "External redirect links are rejected");
    content.Slides[0].Link = "/products";
    var draft = await uploads.StageAsync(new MemoryStream([137,80,78,71,13,10,26,10]), "photo.png");
    Check(await settings.SaveAsync(AccountAccess.Principal(owner), content, new Dictionary<string,ProductDraftUploads.Upload> { ["type-" + type.Id] = draft }) is null, "Type photo is promoted from private staging only at Save");
    saved = await settings.ReadAsync();
    Check(saved.TypePhotos[type.Id].StartsWith("/uploads/images/") && File.Exists(Path.Combine(environment.WebRootPath, saved.TypePhotos[type.Id].TrimStart('/'))), "Saved type photo points to permanent storage");
    Check(uploads.OpenPreview(draft.Name) is null, "Successful save clears the corresponding temporary file");
    Check(await new ProductTypePhotos(factory, settings).ReadAsync(type.Id) == saved.TypePhotos[type.Id], "Administrator photo overrides the default across the store");
    Check(HomepageSettings.DefaultPhoto("تیشرت")!.Contains("tshirt-black") && HomepageSettings.DefaultPhoto("لانگ اسلیو")!.Contains("long-sleeve-black") && HomepageSettings.DefaultPhoto("هودی")!.Contains("hoodie-black"), "All three product types use the supplied plain black garment defaults");
    staff.IsDisabled = true; await db.SaveChangesAsync();
    Check(await settings.SaveAsync(AccountAccess.Principal(staff), content, new Dictionary<string,ProductDraftUploads.Upload>()) is not null, "Disabled seller cannot edit the homepage");
    Console.WriteLine($"{checks} homepage and OTP checks passed; no real SMS sent.");
}
finally
{
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    var resolved = Path.GetFullPath(folder);
    if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(resolved).StartsWith("papey-home-otp-")) Directory.Delete(resolved, true);
}
sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext() => new(options); }
sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class FakeSms : HttpMessageHandler
{
    public int Calls; public bool Fail;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Interlocked.Increment(ref Calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fail ? "{\"status\":0}" : "{\"status\":1,\"data\":{\"messageId\":123}}") }); }
}
