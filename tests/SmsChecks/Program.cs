using System.Net;
using System.Text.Json;
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

var checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new Exception(description);
    checks++;
    Console.WriteLine("PASS: " + description);
}
var values = new Dictionary<string, string?> { ["SMSIR_API_KEY"] = "test-production", ["SMSIR_SANDBOX_API_KEY"] = "test-sandbox", ["SMSIR_LINE_NUMBER"] = "500000000000000", ["SMSIR_OTP_TEMPLATE_ID"] = "900001", ["SMSIR_SHIPPING_TEMPLATE_ID"] = "900002" };
var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var handler = new FakeSmsHandler();
SmsService MakeSms(string environment = "Development", IConfiguration? configuration = null) => new(new HttpClient(handler), configuration ?? config, new TestEnvironment(environment), NullLogger<SmsService>.Instance);
var sms = MakeSms();
Check(SmsService.NormalizePhone("۰۹۱۲۳۴۵۶۷۸۹") == "09123456789" && SmsService.NormalizePhone("+989123456789") == "09123456789" && SmsService.NormalizePhone("00989123456789") == "09123456789", "Phone formats normalize consistently");
Check(SmsService.NormalizePhone("0912bad6789") is null, "Invalid phones rejected");
var result = await sms.SendOtpAsync("09123456789", "12345");
using (var payload = JsonDocument.Parse(handler.Body))
{
    Check(result.Success && handler.Key == "test-sandbox" && handler.Path == "/v1/send/verify" && payload.RootElement.GetProperty("templateId").GetInt32() == 123456, "Development uses sandbox Verify template and key");
    Check(payload.RootElement.GetProperty("parameters")[0].GetProperty("name").GetString() == "CODE", "OTP parameter matches template");
}
var production = MakeSms("Production");
await production.SendOtpAsync("09123456789", "12345");
Check(handler.Key == "test-production" && handler.Body.Contains("900001"), "Production uses production key and approved OTP template");
await production.SendShippingAsync("09123456789", "ORDER-123");
Check(handler.Body.Contains("ORDERNUMBER") && handler.Body.Contains("900002"), "Shipping uses its own Verify template");
handler.Json = "{\"status\":1,\"data\":{\"messageIds\":[123,0,null],\"packId\":\"test-pack\"}}";
result = await sms.SendBulkAsync(["09123456789", "09123456789", "09123456780", "09123456781"], "Test message");
Check(!result.Success && result.Accepted == 1 && result.Rejected == 2 && handler.Body.Contains("sendDateTime"), "Bulk deduplicates and reports partial provider rejection");
var calls = handler.Calls;
result = await sms.SendBulkAsync(["invalid"], "Test");
Check(!result.Success && handler.Calls == calls, "Invalid bulk input never contacts provider");
handler.Json = "{\"status\":1,\"data\":{\"messageIds\":[123]}}";
result = await sms.SendBulkAsync(["09123456789", "09123456780"], "Test");
Check(result.Uncertain, "Incomplete provider result is not reported as success");
handler.Json = "{\"status\":0,\"message\":\"no credit\"}";
result = await sms.SendOtpAsync("09123456789", "12345");
Check(!result.Success && !result.Uncertain, "HTTP 200 provider rejection fails OTP send");
handler.Throw = true;
Check((await sms.SendOtpAsync("09123456789", "12345")).Uncertain, "Network ambiguity is explicit");
handler.Throw = false;
handler.Status = HttpStatusCode.ServiceUnavailable;
Check((await sms.SendOtpAsync("09123456789", "12345")).Uncertain, "Gateway failures cannot trigger duplicate automatic sends");
handler.Status = HttpStatusCode.OK;
var sandboxProduction = MakeSms("Production", new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["SMSIR_SANDBOX"] = "true", ["SMSIR_SANDBOX_API_KEY"] = "test" }).Build());
Check(!sandboxProduction.CanSendOtp, "Production refuses simulated OTP login");
var fallbackConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["SMSIR_API_KEY"] = "test-production", ["SMSIR_LINE_NUMBER"] = "500000000000000" }).Build();
handler.Reset();
await MakeSms("Production", fallbackConfig).SendOtpAsync("09123456789", "12345");
Check(handler.Path == "/v1/send/bulk" && handler.Key == "test-production", "Assigned sender line works before templates are approved");

DotNetEnv.Env.NoClobber().TraversePath().Load();
var localConfig = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? DatabaseConnection.Build(localConfig)) { Pooling = false };
var database = "papey_sms_checks_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
source.Database = database;
var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
try
{
    await using var db = factory.CreateDbContext();
    await db.Database.MigrateAsync();
    Order Order(OrderStatus status) => new() { OrderNumber = "SMS-"+Guid.NewGuid().ToString("N")[..12], CustomerName = "Test", CustomerPhone = "09123456789", Province = "Test", City = "Test", FullAddress = "Test", Status = status, PaidAt = status == OrderStatus.Paid ? DateTime.UtcNow : null };
    var order = Order(OrderStatus.Paid);
    var unpaid = Order(OrderStatus.Pending);
    db.Orders.AddRange(order, unpaid);
    await db.SaveChangesAsync();
    var queue = new ShippingSmsQueue(factory, production);
    Check(!await queue.SetOrderStatusAsync(unpaid.Id, OrderStatus.Shipped), "Unpaid orders cannot be shipped manually");
    Check(!await queue.SetOrderStatusAsync(order.Id, (OrderStatus)999), "Undefined statuses are rejected");
    await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => queue.SetOrderStatusAsync(order.Id, OrderStatus.Shipped)));
    Check(await db.ShippingSmsJobs.CountAsync(j => j.OrderId == order.Id) == 1, "Concurrent shipping transitions queue exactly once");
    await queue.SetOrderStatusAsync(order.Id, OrderStatus.Processing);
    await queue.SetOrderStatusAsync(order.Id, OrderStatus.Shipped);
    Check(await db.ShippingSmsJobs.CountAsync(j => j.OrderId == order.Id) == 1, "Status toggles do not duplicate notification");
    handler.Reset();
    var processed = await Task.WhenAll(queue.ProcessOneAsync(), queue.ProcessOneAsync());
    Check(processed.Count(p => p) == 1 && handler.Calls == 1 && await db.ShippingSmsJobs.AnyAsync(j => j.State == ShippingSmsState.Accepted), "Concurrent workers send once and persist acceptance");
    Check(!await queue.RetryAsync(await db.ShippingSmsJobs.Select(j => j.Id).FirstAsync()), "Accepted messages cannot be retried");

    var failed = Order(OrderStatus.Paid);
    db.Orders.Add(failed); await db.SaveChangesAsync();
    await queue.SetOrderStatusAsync(failed.Id, OrderStatus.Shipped);
    handler.Json = "{\"status\":0}";
    for (var i = 0; i < 3; i++)
    {
        await db.ShippingSmsJobs.Where(j => j.OrderId == failed.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.AvailableAt, DateTime.UtcNow.AddMinutes(-1)));
        await queue.ProcessOneAsync();
    }
    var failedId = await db.ShippingSmsJobs.Where(j => j.OrderId == failed.Id && j.State == ShippingSmsState.Failed).Select(j => j.Id).SingleAsync();
    Check(await queue.RetryAsync(failedId), "Definite failures persist and can be manually retried");
    handler.Throw = true;
    await queue.ProcessOneAsync();
    Check(await db.ShippingSmsJobs.AnyAsync(j => j.Id == failedId && j.State == ShippingSmsState.Unknown) && !await queue.RetryAsync(failedId), "Unknown delivery is never automatically resent");
    handler.Throw = false;

    var sandboxOrder = Order(OrderStatus.Paid); db.Orders.Add(sandboxOrder); await db.SaveChangesAsync();
    var sandboxQueue = new ShippingSmsQueue(factory, sms);
    await sandboxQueue.SetOrderStatusAsync(sandboxOrder.Id, OrderStatus.Shipped);
    Check(!await queue.ProcessOneAsync(), "Production worker cannot send sandbox jobs");
    await sandboxQueue.SetOrderStatusAsync(sandboxOrder.Id, OrderStatus.Cancelled);
    await sandboxQueue.ProcessOneAsync();
    Check(await db.ShippingSmsJobs.AnyAsync(j => j.OrderId == sandboxOrder.Id && j.State == ShippingSmsState.Cancelled), "Cancelled orders suppress queued shipping messages");

    var auth = new AuthService(factory, new HttpContextAccessor(), config, sms, new TestEnvironment("Development"));
    handler.Reset();
    var otp = await auth.RequestOtpAsync("۰۹۱۲۳۴۵۶۷۸۹");
    Check(otp.Success && otp.DevCode is { Length: 5 }, "Confirmed sandbox OTP is available only in Development");
    Check(!await db.Users.AnyAsync(u => u.Phone == "09123456789"), "Requesting a code cannot register an unverified phone");
    Check(!(await auth.RequestOtpAsync("09123456789")).Success, "OTP resend cooldown is enforced");
    var users = await Task.WhenAll(auth.VerifyOtpAndSignInAsync("09123456789", otp.DevCode!), auth.VerifyOtpAndSignInAsync("09123456789", otp.DevCode!));
    Check(users.Count(u => u is not null) == 1, "Concurrent OTP redemption is single-use");
    var phoneUser = users.Single(u => u is not null)!;
    Check(phoneUser.Phone == "09123456789" && phoneUser.Username == "09123456789" && !phoneUser.IsAdmin,
        "Verified new phone automatically receives an ordinary account");
    db.OtpCodes.Add(new OtpCode { Phone = "09123456789", Code = "54321", ExpiresAt = DateTime.UtcNow.AddMinutes(2) });
    await db.SaveChangesAsync();
    Check((await auth.VerifyOtpAndSignInAsync("09123456789", "54321"))?.Id == phoneUser.Id &&
        await db.Users.CountAsync(u => u.Phone == "09123456789") == 1, "Verified existing phone signs into the same account");
    handler.Json = "{\"status\":0}";
    var failedOtp = await auth.RequestOtpAsync("09123456780");
    Check(!failedOtp.Success && failedOtp.DevCode is null && !await db.OtpCodes.AnyAsync(o => o.Phone == "09123456780" && !o.IsUsed), "Rejected OTP is invalidated and not shown as sent");
    var prodAuth = new AuthService(factory, new HttpContextAccessor(), config, production, new TestEnvironment("Production"));
    handler.Reset();
    Check((await prodAuth.RequestOtpAsync("09123456781")).DevCode is null, "Production never returns OTP in its response");
    Check(!(await auth.RequestOtpAsync("09123456782", requireExistingUser: true)).Success, "Password reset refuses unregistered phones");
    await db.OtpCodes.Where(o => o.Phone == "09123456789").ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    var resetOtp = await auth.RequestOtpAsync("09123456789", requireExistingUser: true);
    Check(!(await auth.ResetPasswordByOtpAsync("09123456789", "wrong", "test-password")).Success, "Wrong reset codes are rejected");
    Check((await auth.ResetPasswordByOtpAsync("09123456789", resetOtp.DevCode!, "test-password")).Success &&
        await auth.ValidateLoginAsync("09123456789", "test-password") is not null, "OTP password reset persists the new password");
    Check(!(await auth.ResetPasswordByOtpAsync("09123456789", resetOtp.DevCode!, "another-password")).Success, "Password-reset OTP cannot be replayed");
    Check(!db.Database.HasPendingModelChanges(), "Migration snapshot matches the model");
}
finally
{
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
}
if (Environment.GetEnvironmentVariable("PAPEY_SMS_SANDBOX_CHECK") == "true")
{
    var live = new SmsService(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, localConfig, new TestEnvironment("Development"), NullLogger<SmsService>.Instance);
    var response = await live.SendOtpAsync("09120000000", "12345");
    Check(response.Success && live.IsSandbox, "Live SMS.ir sandbox Verify accepts development request (no real SMS)");
    Check((await live.SendBulkAsync(["09120000000"], "Papey sandbox check")).Success, "Live SMS.ir sandbox bulk accepts development request (no real SMS)");
}
Console.WriteLine($"{checks} SMS checks passed.");

sealed class FakeSmsHandler : HttpMessageHandler
{
    public string Json = "{\"status\":1,\"data\":{\"messageId\":123,\"messageIds\":[123],\"packId\":\"test-pack\"}}";
    public string Body = "", Key = "", Path = "";
    public int Calls;
    public bool Throw;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public void Reset() { Calls=0; Throw=false; Json="{\"status\":1,\"data\":{\"messageId\":123,\"messageIds\":[123],\"packId\":\"test-pack\"}}"; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (Throw) throw new HttpRequestException("Simulated transport failure");
        Key=request.Headers.GetValues("x-api-key").Single(); Path=request.RequestUri!.AbsolutePath;
        Body=await request.Content!.ReadAsStringAsync(cancellationToken);
        return new(Status) { Content=new StringContent(Json) };
    }
}
sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "SmsChecks";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
}
