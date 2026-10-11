using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
}

static Order NewOrder(OrderStatus status = OrderStatus.Pending) => new()
{
    OrderNumber = "CHECK-" + Guid.NewGuid().ToString("N")[..12],
    CustomerName = "Printing check", CustomerPhone = "09120000000", Province = "Test",
    City = "Test", FullAddress = "Test address", Status = status,
    PaidAt = status == OrderStatus.Paid ? DateTime.UtcNow : null,
    PaymentRefId = status == OrderStatus.Paid ? "test-ref" : null
};

DotNetEnv.Env.NoClobber().TraversePath().Load();
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION")
    ?? DatabaseConnection.Build(config)) { Pooling = false };
var temporaryDatabase = "papey_print_checks_" + Guid.NewGuid().ToString("N");
var adminConnection = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" };
await using var admin = new NpgsqlConnection(adminConnection.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{temporaryDatabase}\"", admin).ExecuteNonQueryAsync();
source.Database = temporaryDatabase;
var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
Process? server = null;
try
{
    await using var db = factory.CreateDbContext();
    await db.GetService<IMigrator>().MigrateAsync("20261009170034_AddProductTypeFilterIcon");
    db.Orders.Add(NewOrder(OrderStatus.Paid));
    await db.SaveChangesAsync();
    await db.Database.MigrateAsync();
    Check(!await db.OrderPrintJobs.AnyAsync(), "Migration does not print historical paid orders");

    var order = NewOrder();
    db.Orders.Add(order);
    db.Orders.Add(NewOrder(OrderStatus.PaymentFailed));
    await db.SaveChangesAsync();
    Check(!await db.OrderPrintJobs.AnyAsync(), "Pending and failed orders do not enter the queue");
    var orderId = order.Id;

    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTime.UtcNow;
        order.PaymentRefId = "verified-test-ref";
        await db.SaveChangesAsync();
        Check(await db.OrderPrintJobs.CountAsync() == 1, "Successful payment creates a job in the payment transaction");
        await transaction.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Check(!await db.OrderPrintJobs.AnyAsync(), "Rolling back payment also rolls back its print job");
    order = await db.Orders.SingleAsync(o => o.Id == orderId);
    order.Status = OrderStatus.Paid;
    order.PaidAt = DateTime.UtcNow;
    order.PaymentRefId = "verified-test-ref";
    await db.SaveChangesAsync();
    order.PaidAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    Check(await db.OrderPrintJobs.CountAsync() == 1, "Repeated payment saves do not duplicate jobs");

    var queue = new OrderPrintQueue(factory);
    var claims = await Task.WhenAll(queue.ClaimAsync(42, "printer-a", default), queue.ClaimAsync(42, "printer-b", default));
    Check(claims.Count(c => c is not null) == 1, "Concurrent printers cannot claim the same job");
    var lease = claims.Single(c => c is not null)!;
    var clientId = claims[0] is not null ? "printer-a" : "printer-b";
    Check(!await queue.CompleteAsync(lease.JobId, 43, clientId, lease.LeaseToken, default), "Another admin cannot acknowledge the lease");
    Check(await queue.RenewAsync(lease.JobId, 42, clientId, lease.LeaseToken, default) is not null, "Active lease can be renewed");
    Check(await queue.ReleaseAsync(lease.JobId, 42, clientId, lease.LeaseToken, "Test printer offline", default), "Failed printing can release its job");
    Check(await queue.ClaimAsync(42, clientId, default) is null, "Released jobs respect the retry delay");
    await db.OrderPrintJobs.Where(j => j.Id == lease.JobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.AvailableAt, DateTime.UtcNow.AddSeconds(-1)));
    var retry = (await queue.ClaimAsync(42, clientId, default))!;
    Check(retry.Attempts == 2 && retry.LeaseToken != lease.LeaseToken, "Retry issues a fresh lease and tracks attempts");
    await db.OrderPrintJobs.Where(j => j.Id == lease.JobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
    var recovered = (await new OrderPrintQueue(factory).ClaimAsync(42, "printer-c", default))!;
    Check(recovered.JobId == lease.JobId && recovered.Attempts == 3, "A new service instance recovers an abandoned lease");
    Check(!await queue.CompleteAsync(retry.JobId, 42, clientId, retry.LeaseToken, default), "Reclaimed jobs reject stale acknowledgements");
    Check(await queue.CompleteAsync(recovered.JobId, 42, "printer-c", recovered.LeaseToken, default)
        && await queue.CompleteAsync(recovered.JobId, 42, "printer-c", recovered.LeaseToken, default), "Acknowledgement is idempotent");
    Check(await queue.ClaimAsync(42, clientId, default) is null, "Completed jobs cannot be reclaimed");
    var cancelled = NewOrder(OrderStatus.Paid);
    db.Orders.Add(cancelled);
    await db.SaveChangesAsync();
    cancelled.Status = OrderStatus.Cancelled;
    await db.SaveChangesAsync();
    Check(await queue.ClaimAsync(42, clientId, default) is null, "Cancelled orders are excluded from printing");

    // Run the actual application against only this disposable database.
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "Papey.csproj"))) root = root.Parent;
    if (root is null) throw new Exception("Cannot find the project root.");
    var buildConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var appDll = Path.Combine(root.FullName, "bin", buildConfiguration, "net10.0", "Papey.dll");
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    var baseUrl = $"http://127.0.0.1:{port}";
    var password = Guid.NewGuid().ToString("N");
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root.FullName };
    foreach (var argument in new[] { appDll, "--urls", baseUrl, "--contentRoot", root.FullName }) start.ArgumentList.Add(argument);
    start.Environment["ConnectionStrings__DefaultConnection"] = source.ConnectionString;
    start.Environment["ADMIN_USERNAME"] = "printing-check-admin";
    start.Environment["ADMIN_PASSWORD"] = password;
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    start.Environment["Deployment__HttpsOnly"] = "false";
    start.Environment["Logging__LogLevel__Default"] = "Warning";
    start.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";
    var serverLog = new System.Collections.Concurrent.ConcurrentQueue<string>();
    server = new Process { StartInfo = start };
    server.OutputDataReceived += (_, e) => { if (e.Data is not null) serverLog.Enqueue(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data is not null) serverLog.Enqueue(e.Data); };
    server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
    var cookies = new CookieContainer();
    using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false })
        { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    var ready = false;
    for (var attempt = 0; attempt < 100 && !server.HasExited; attempt++)
    {
        try { ready = (await http.GetAsync("/healthz")).IsSuccessStatusCode; } catch (HttpRequestException) { }
        if (ready) break;
        await Task.Delay(200);
    }
    if (!ready) throw new Exception("Test server failed: " + string.Join(Environment.NewLine, serverLog));
    const string api = "/api/admin/printing";
    Check((await http.GetAsync(api + "/session")).StatusCode == HttpStatusCode.Unauthorized, "Printing API rejects anonymous clients without redirecting");
    Check((await http.PostAsync("/hubs/admin/printing/negotiate?negotiateVersion=1", null)).StatusCode == HttpStatusCode.Unauthorized,
        "Printing hub rejects anonymous connections");
    Check((await http.PostAsJsonAsync(api + "/login", new { identifier = "printing-check-admin", password })).StatusCode == HttpStatusCode.NoContent,
        "Admin desktop login issues a usable session");
    Check((await http.PostAsJsonAsync(api + "/jobs/claim", new { clientId = "api-printer" })).StatusCode == HttpStatusCode.BadRequest,
        "Queue mutations require an antiforgery token");
    var session = await http.GetFromJsonAsync<JsonElement>(api + "/session");
    http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());

    using var socket = new ClientWebSocket();
    socket.Options.Cookies = cookies;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/hubs/admin/printing"), deadline.Token);
    await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, deadline.Token);
    var buffer = new byte[4096];
    var notification = "";
    while (!notification.Contains("PrintJobsAvailable"))
    {
        var received = await socket.ReceiveAsync(buffer, deadline.Token);
        notification += Encoding.UTF8.GetString(buffer, 0, received.Count);
    }
    Check(true, "Authenticated SignalR connection receives a queue notification");
    var apiOrder = NewOrder(OrderStatus.Paid);
    db.Orders.Add(apiOrder);
    await db.SaveChangesAsync();
    notification = "";
    while (!notification.Contains("PrintJobsAvailable"))
    {
        var received = await socket.ReceiveAsync(buffer, deadline.Token);
        notification += Encoding.UTF8.GetString(buffer, 0, received.Count);
    }
    Check(true, "A newly paid order wakes the connected desktop printer");
    var response = await http.PostAsJsonAsync(api + "/jobs/claim", new { clientId = "api-printer" });
    response.EnsureSuccessStatusCode();
    var apiLease = (await response.Content.ReadFromJsonAsync<PrintJobLease>())!;
    Check(apiLease.OrderId == apiOrder.Id, "HTTP claim returns the newly paid order");
    var receipt = await http.GetStringAsync(apiLease.ReceiptUrl);
    Check(receipt.Contains("data-print-ready=\"true\"") && receipt.Contains($"data-order-id=\"{apiOrder.Id}\""),
        "Claim points to the existing authenticated admin receipt page");
    Check((await http.PostAsJsonAsync($"{api}/jobs/{apiLease.JobId}/complete", new { clientId = "api-printer", apiLease.LeaseToken })).StatusCode == HttpStatusCode.NoContent,
        "HTTP acknowledgement completes printing");
    Check((await http.PostAsJsonAsync(api + "/jobs/claim", new { clientId = "api-printer" })).StatusCode == HttpStatusCode.NoContent,
        "HTTP queue is drained after acknowledgement");
    socket.Abort();
}
finally
{
    if (server is not null)
    {
        if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
        server.Dispose();
    }
    NpgsqlConnection.ClearAllPools();
    // The only deletion is the GUID-named database created above by this test run.
    await new NpgsqlCommand($"DROP DATABASE \"{temporaryDatabase}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
}

sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
