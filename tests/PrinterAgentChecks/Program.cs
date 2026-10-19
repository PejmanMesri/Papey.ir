using System.Net;
using System.Net.Http.Json;
using Papey.Printer;

var folder = Path.Combine(Path.GetTempPath(), "papey-printer-checks-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("PAPEY_PRINTER_DATA", folder);
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
try
{
    Check(ThermalReceipt.WidthFor("BIXOLON SRP-330II") == 512 && ThermalReceipt.WidthFor("BIXOLON SRP-332II") == 576,
        "Raster width matches the 180 dpi / 203 dpi BIXOLON model");
    using (var image = new System.Drawing.Bitmap(512, 2000))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(image))
        { graphics.Clear(System.Drawing.Color.White); graphics.FillRectangle(System.Drawing.Brushes.Black, 40, 100, 400, 80); }
        var receipt = ThermalReceipt.Create(image, 512);
        Check(receipt.Height < 120, "Blank page padding is cropped and short receipts have no 100 mm minimum");
        Check(receipt.Commands[^4..].SequenceEqual(new byte[] { 0x1d, 0x56, 66, 0 }), "Receipt ends with a single minimum-feed cut, not a form feed");
        var offset = 5; var rows = 0; var chunks = 0;
        while (offset < receipt.Commands.Length - 4)
        {
            if (!receipt.Commands[offset..(offset + 4)].SequenceEqual(new byte[] { 0x1d, 0x76, 0x30, 0 })) throw new Exception("Invalid raster command");
            var bytesWide = receipt.Commands[offset + 4] + 256 * receipt.Commands[offset + 5];
            var height = receipt.Commands[offset + 6] + 256 * receipt.Commands[offset + 7];
            if (bytesWide != 64 || height is < 1 or > 256) throw new Exception("Invalid raster dimensions");
            rows += height; chunks++; offset += 8 + bytesWide * height;
        }
        Check(rows == receipt.Height && offset == receipt.Commands.Length - 4 && chunks > 0,
            "Printer receives exactly the cropped receipt rows without page-length padding");
    }
    using (var tall = new System.Drawing.Bitmap(512, 3000))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(tall))
        { graphics.Clear(System.Drawing.Color.White); graphics.FillRectangle(System.Drawing.Brushes.Black, 20, 0, 400, 2500); }
        var receipt = ThermalReceipt.Create(tall, 512);
        var offset = 5; var rows = 0; var chunks = 0;
        while (offset < receipt.Commands.Length - 4)
        {
            var height = receipt.Commands[offset + 6] + 256 * receipt.Commands[offset + 7];
            if (height is < 1 or > 256) throw new Exception("Unsafe raster chunk size");
            rows += height; chunks++; offset += 8 + 64 * height;
        }
        Check(chunks > 1 && rows == receipt.Height && rows >= 2500 && offset == receipt.Commands.Length - 4,
            "Long invoices retain all rows across bounded raster chunks before one final cut");
    }
    using (var blank = new System.Drawing.Bitmap(512, 50))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(blank)) graphics.Clear(System.Drawing.Color.White);
        try { ThermalReceipt.Create(blank, 512); throw new Exception("Blank receipt accepted"); }
        catch (InvalidOperationException) { Check(true, "Blank captures cannot feed blank receipt paper"); }
    }
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
