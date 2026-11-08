using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
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

int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION") ?? throw new Exception("Explicit disposable connection required")) { Pooling = false };
var database = "papey_staff_checks_" + Guid.NewGuid().ToString("N");
await using var adminConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
await adminConnection.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection).ExecuteNonQueryAsync();
source.Database = database;
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options;
var factory = new Factory(options);
var testPath = Path.Combine(Path.GetTempPath(), "papey-staff-keys-" + Guid.NewGuid().ToString("N"));
Process? server = null;
try
{
    await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
    const string ownerPassword = "OwnerCheck!2026";
    const string staffPassword = "StaffCheck!2026";
    const string newPassword = "NewStaffCheck!2026";
    var owner = new AppUser { Username = "OwnerTest", IsAdmin = true, PasswordHash = BCrypt.Net.BCrypt.HashPassword(ownerPassword) };
    var customer = new AppUser { Username = "CustomerTest", PasswordHash = "unused" };
    db.Users.AddRange(owner, customer); await db.SaveChangesAsync();
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ADMIN_PHONES"] = "09022121464", ["ADMIN_USERNAME"] = "OwnerTest" }).Build();
    var environment = new TestEnvironment();
    var sms = new SmsService(new HttpClient(new NoNetwork()), config, environment, NullLogger<SmsService>.Instance);
    AuthService Auth(AppUser? user) => new(factory, new FixedAccessor { HttpContext = new DefaultHttpContext { User = user is null ? new(new ClaimsIdentity()) : AccountAccess.Principal(user) } }, config, sms, environment);
    var ownerAuth = Auth(owner); var accounts = new StaffAccounts(factory, ownerAuth);
    Check(AccountAccess.Principal(owner).IsInRole("Owner") && AccountAccess.Principal(owner).IsInRole("Admin"), "Existing main administrator owns staff management and retains all admin permissions");
    Check(await accounts.CreateAsync("support.one", "Support Test", "09121112222", staffPassword) is null, "Owner can create a staff account");
    Check(await accounts.CreateAsync("too.long", "Invalid password", null, new string('x', 73)) is not null,
        "Oversized passwords are rejected before hashing rather than being silently truncated");
    var staff = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "support.one");
    var staffPrincipal = AccountAccess.Principal(staff);
    using var circuitState = new StoreAuthenticationStateProvider(NullLoggerFactory.Instance, factory);
    circuitState.SetAuthenticationState(Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(staffPrincipal)));
    var actions = 0;
    var activity = new AccountCircuitHandler(circuitState, factory).CreateInboundActivityHandler(_ => { actions++; return Task.CompletedTask; });
    await activity(null!);
    Check(actions == 1, "A current active staff session can execute Blazor actions");
    Check(staff.IsStaff && !staff.IsAdmin && staffPrincipal.IsInRole("Admin") && staffPrincipal.IsInRole("Staff") && !staffPrincipal.IsInRole("Owner"), "Staff gets shop administration but cannot become the owner");
    Check(BCrypt.Net.BCrypt.Verify(staffPassword, staff.PasswordHash), "Staff passwords are stored as hashes");
    Check(LoginRedirect.ForUser(staff, "/checkout") == "/admin", "Staff login redirects to the admin dashboard");
    var staffAuth = Auth(staff); var staffAccounts = new StaffAccounts(factory, staffAuth);
    Check(await staffAccounts.CreateAsync("nested.staff", "Forbidden", null, staffPassword) is not null, "Staff cannot create other staff accounts");
    Check(!await staffAccounts.SetDisabledAsync(owner.Id, true) && await staffAccounts.ResetPasswordAsync(staff.Id, newPassword) is not null, "Staff cannot disable or reset any managed account");
    var refused = false; try { await staffAccounts.ReadAsync(); } catch (UnauthorizedAccessException) { refused = true; }
    Check(refused, "Staff cannot retrieve the owner-only account list");
    Check(!await accounts.SetDisabledAsync(owner.Id, true) && await accounts.ResetPasswordAsync(owner.Id, newPassword) is not null, "Staff management cannot modify or disable the main owner");
    Check(await accounts.CreateAsync("SUPPORT.ONE", "Duplicate", null, staffPassword) is not null && await accounts.CreateAsync("reserved.phone", "Reserved", "09022121464", staffPassword) is not null, "Duplicate usernames and reserved owner phone aliases are rejected");
    var ordinary = await ownerAuth.RegisterAsync("normal.user", "customer-password", null, "09121113333", "Customer");
    Check(ordinary.User is { IsAdmin: false, IsStaff: false }, "Public signup can never grant a staff role");
    var category = new Category { Name = "Test", Slug = "test" };
    var product = new Product { Name = "Test", Slug = "test", Category = category, Variants = [new() { Color = "Black", Size = "M" }] };
    var order = new Order { User = customer, OrderNumber = "ROLE-CHECK", CustomerName = "Test", CustomerPhone = "09120000000", Province = "Test", City = "Test", FullAddress = "Test", PaidAt = DateTime.UtcNow, Status = OrderStatus.Paid };
    order.Items.Add(new() { Product = product, ProductVariant = product.Variants[0], ProductName = "Test", Color = "Black", Size = "M", Quantity = 1 });
    var review = new ProductReview { Product = product, User = customer, Order = order, Comment = "Good purchase", PrintQuality = 5, FabricQuality = 4, Fit = ReviewFit.TrueToSize };
    db.ProductReviews.Add(review); await db.SaveChangesAsync();
    Check(await new ProductReviewService(factory, staffAuth).ReplyAsync(review.Id, "Site manager reply"), "Staff can answer buyer reviews");
    Check((await new ProductReviewService(factory, staffAuth).ReadAsync(product.Id)).Reviews.Single().Reply == "Site manager reply", "Staff reply is visible on the customer product page");

    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var origin = $"http://127.0.0.1:{port}";
    var start = new ProcessStartInfo("dotnet", $"bin/Debug/net10.0/Papey.dll --urls {origin}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.Environment["ConnectionStrings__DefaultConnection"] = source.ConnectionString;
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    start.Environment["Deployment__HttpsOnly"] = "false";
    start.Environment["Deployment__RequireSecureConfiguration"] = "false";
    start.Environment["DataProtection__ApplicationName"] = "Papey.StaffChecks";
    start.Environment["DataProtection__KeyPath"] = testPath;
    start.Environment["ADMIN_USERNAME"] = owner.Username;
    start.Environment["ADMIN_PASSWORD"] = ownerPassword;
    start.Environment["SMSIR_SANDBOX"] = "true";
    start.Environment["SMSIR_SANDBOX_API_KEY"] = "YOUR_DISABLED_TEST_KEY";
    start.Environment["SMSIR_API_KEY"] = "YOUR_DISABLED_TEST_KEY";
    start.Environment["ORDER_NOTIFICATION_PHONES"] = "";
    server = Process.Start(start)!; var log = server.StandardOutput.ReadToEndAsync(); var errors = server.StandardError.ReadToEndAsync();
    using var ownerClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) };
    for (var attempt = 0; attempt < 80; attempt++) { try { if ((await ownerClient.GetAsync("/healthz")).IsSuccessStatusCode) break; } catch { } if (server.HasExited) throw new Exception("Test server failed: " + await errors); await Task.Delay(250); }
    async Task<bool> Login(HttpClient client, string name, string password) => (await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["identifier"] = name, ["password"] = password }))).Headers.Location?.OriginalString == "/admin";
    using var staffClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) };
    Check(await Login(ownerClient, owner.Username, ownerPassword) && await Login(staffClient, staff.Username, staffPassword), "Owner and staff can sign in over real HTTP");
    Check((await ownerClient.GetStringAsync("/admin/staff")).Contains("staff-username"), "Owner can open the staff management form");
    var denied = await staffClient.GetAsync("/admin/staff");
    Check(!denied.IsSuccessStatusCode && !(await denied.Content.ReadAsStringAsync()).Contains("staff-username"), "Staff is denied the owner-only route on the server");
    foreach (var path in new[] { "/admin", "/admin/products", "/admin/reviews", "/admin/sales", "/admin/sms" })
        Check((await staffClient.GetAsync(path)).IsSuccessStatusCode, "Staff retains access to " + path);
    Check(!(await staffClient.GetAsync("/admin/settings")).IsSuccessStatusCode, "Seller cannot open owner-only settings");
    Check((await ownerClient.GetStringAsync("/admin/settings")).Contains("guide-settings-title"), "Owner settings include product-type size guides");
    var sellerDashboard=WebUtility.HtmlDecode(await staffClient.GetStringAsync("/admin"));
    Check(sellerDashboard.Contains("میز کار فروشنده")&&!sellerDashboard.Contains("فروش ۳۰ روز اخیر"), "Seller dashboard focuses on fulfillment rather than owner analytics");
    Check((await staffClient.GetStringAsync("/")).Contains("admin-storefront-return"), "Staff sees the storefront return-to-admin link");
    Check(WebUtility.HtmlDecode(await staffClient.GetStringAsync("/admin/vouchers")).Contains("تعریف کد تخفیف"), "Seller can open voucher administration");
    using var buyerClient=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}) {BaseAddress=new Uri(origin)};
    await buyerClient.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"identifier","normal.user"},{"password","customer-password"}}));
    var unpaidOrder=new Order {OrderNumber="UNPAID-CHECK",UserId=ordinary.User!.Id,CustomerName="Test",CustomerPhone="09121113333",Province="Test",City="Test",FullAddress="Test"};db.Orders.Add(unpaidOrder);await db.SaveChangesAsync();
    var orderList=await buyerClient.GetStringAsync("/account/orders");Check(!orderList.Contains($"/account/orders/{unpaidOrder.Id}/invoice"),"Unpaid order list offers details without invoice buttons");
    foreach(var format in new[]{"thermal","standard","official"}) {var document=await buyerClient.GetStringAsync($"/account/orders/{unpaidOrder.Id}/invoice/{format}");Check(!document.Contains("<article class=\"thermal-receipt")&&!document.Contains("<article class=\"standard-invoice")&&!document.Contains("<article class=\"official-invoice"),"Unpaid "+format+" invoice cannot render through direct URL");}
    var info=await buyerClient.GetStringAsync($"/account/orders/{unpaidOrder.Id}");Check(!info.Contains($"/account/orders/{unpaidOrder.Id}/invoice"),"Unpaid order details do not offer invoices");
    Check(!(await buyerClient.GetAsync("/admin/vouchers")).IsSuccessStatusCode,"Customer cannot access voucher administration route");
    var about=await buyerClient.GetStringAsync("/about");Check(about.Contains("https://t.me/papeycompany")&&about.Contains("social-links"),"About page includes Telegram card and social logo links");
    Check(await accounts.ResetPasswordAsync(staff.Id, newPassword) is null, "Owner can reset a staff password");
    await activity(null!);
    var staleAuth = new AuthService(factory, new FixedAccessor { HttpContext = new DefaultHttpContext { User = staffPrincipal } }, config, sms, environment, circuitState);
    Check(actions == 1 && staleAuth.CurrentUserId is null, "Password reset blocks an already-open circuit action and clears its effective identity");
    var stale = await staffClient.GetAsync("/admin");
    Check(!stale.IsSuccessStatusCode && await ownerAuth.ValidateLoginAsync(staff.Username, staffPassword) is null, "Password reset invalidates existing cookies and the old password");
    var updated = (await ownerAuth.ValidateLoginAsync(staff.Username, newPassword))!;
    Check(updated is not null && !AccountAccess.Valid(staffPrincipal, updated), "Old principals cannot survive a staff password reset");
    Check(await Login(staffClient, staff.Username, newPassword), "The replacement staff password can sign in");
    Check(await accounts.SetDisabledAsync(staff.Id, true), "Owner can disable a staff account");
    Check(!((await staffClient.GetAsync("/admin")).IsSuccessStatusCode) && await ownerAuth.ValidateLoginAsync(staff.Username, newPassword) is null, "Disabling staff revokes its existing cookie and blocks password login");
    Check(!(await ownerAuth.RequestOtpAsync(staff.Phone!)).Success && await ownerAuth.GetUserByIdAsync(staff.Id) is null, "Disabled accounts cannot enter via OTP or sign-in tickets");
    Check(!await new ProductReviewService(factory, staffAuth).ReplyAsync(review.Id, "Disabled reply"), "Disabled staff loses reply permissions");
    Check(await accounts.SetDisabledAsync(staff.Id, false) && await ownerAuth.ValidateLoginAsync(staff.Username, newPassword) is not null, "Owner can re-enable staff with its current password");
    Check(!await new ProductReviewService(factory, staffAuth).ReplyAsync(review.Id, "Stale reply"), "Re-enabling staff does not restore old revoked session permissions");
    var legacy = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")], "Cookies"));
    Check(AccountAccess.Valid(legacy, owner), "Pre-upgrade owner cookies remain valid across this deployment");
    Check(!db.Database.HasPendingModelChanges(), "Staff migration matches the current model");
    Console.WriteLine($"{checks} staff permission and session checks passed.");
}
finally
{
    if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", adminConnection).ExecuteNonQueryAsync();
    var resolved = Path.GetFullPath(testPath);
    if (Directory.Exists(resolved) && Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(resolved).StartsWith("papey-staff-keys-")) Directory.Delete(resolved, true);
}
sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext() => new(options); }
sealed class FixedAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = "Development"; public string ApplicationName { get; set; } = "Tests"; public string ContentRootPath { get; set; } = Path.GetTempPath(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new Exception("No SMS requests in staff tests"); }
