using System.Net;
using System.Net.Http.Json;
using Papey.Printer;

var folder = Path.Combine(Path.GetTempPath(), "papey-printer-checks-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("PAPEY_PRINTER_DATA", folder);
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
try
{
    var protectedPassword = Store.Protect("test-only-password");
    Check(protectedPassword != "test-only-password" && Store.Unprotect(protectedPassword) == "test-only-password", "Windows protects saved credentials and can restore them");
    var settings = new Settings("https://papey.ir/", "BIXOLON SRP-330II", "Admin", protectedPassword, "test-printer");
    var lease = new Lease(Guid.NewGuid(), 42, "TEST", "/admin/print/order/42?autoPrint=false", Guid.NewGuid());
    Check(JournalPolicy.Decide(null) == Recovery.None, "An empty journal permits checking the queue");
    Store.Write("pending.json", new Journal(lease, "Prepared"));
    var saved = Store.Read<Journal>("pending.json")!;
    Check(saved.Lease == lease && JournalPolicy.Decide(saved) == Recovery.ReleasePreparation, "Restart before print submission releases preparation safely");
    Check(JournalPolicy.Decide(saved with { State = "Submitting" }) == Recovery.Review, "A crash during submission requires review, never automatic reprinting");
    Check(JournalPolicy.Decide(saved with { State = "Submitted" }) == Recovery.Acknowledge, "A lost acknowledgement retries completion without another print");
    Check(JournalPolicy.Decide(saved with { State = "Unexpected" }) == Recovery.Review, "Unknown journal states stop automatic processing");
    Store.Write<Journal?>("pending.json", null);
    Check(Store.Read<Journal>("pending.json") is null, "Completed jobs clear the pending journal durably");
    var fake = new FakeApi();
    using var api = new PrintingApi(settings, fake);
    await api.Connect();
    Check(fake.LoginCount == 1, "The agent authenticates against the printing API");
    Check(await api.Claim() is null && fake.CsrfSeen, "Empty queues do not create prints and claims include CSRF protection");
    fake.NextLease = lease;
    Check(await api.Claim() == lease && fake.LoginCount == 1, "A paid job lease is deserialized using the existing session");
    Check(!await api.Action(lease, "renew"), "A rejected lease prevents further printing");
    Check(await api.Action(lease, "complete"), "Successful completion is acknowledged with the lease token");
    Check(api.Receipt(lease).Host == "papey.ir", "Receipt rendering uses the authenticated site origin");
    foreach (var bad in new[] { "https://evil.example/receipt", "/login", "/admin/print/order/43?autoPrint=false", "/admin/print/order/42?autoPrint=true" })
    {
        try { api.Receipt(lease with { ReceiptUrl = bad }); throw new Exception("Unsafe receipt accepted"); }
        catch (InvalidOperationException) { Check(true, "Rejects unexpected receipt address"); }
    }
    using var insecure = new PrintingApi(settings with { Server = "http://papey.ir/" }, new FakeApi());
    try { await insecure.Connect(); throw new Exception("Insecure login accepted"); }
    catch (InvalidOperationException) { Check(true, "Credentials are never sent to an HTTP server"); }
    Console.WriteLine($"{checks} printer-agent checks passed.");
}
finally { Directory.Delete(folder, true); }

sealed class FakeApi : HttpMessageHandler
{
    public int LoginCount; public bool CsrfSeen; public Lease? NextLease;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/login")) { LoginCount++; return new(HttpStatusCode.NoContent); }
        if (path.EndsWith("/session")) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { csrfToken = "test-csrf" }) };
        CsrfSeen = request.Headers.GetValues("X-CSRF-TOKEN").Single() == "test-csrf";
        if (!CsrfSeen) throw new Exception("Missing CSRF");
        var body = await request.Content!.ReadAsStringAsync(ct);
        if (!body.Contains("test-printer")) throw new Exception("Missing stable client ID");
        if (path.EndsWith("/claim")) return NextLease is null ? new(HttpStatusCode.NoContent) : new(HttpStatusCode.OK) { Content = JsonContent.Create(NextLease) };
        return new(path.EndsWith("/renew") ? HttpStatusCode.Conflict : HttpStatusCode.NoContent);
    }
}
