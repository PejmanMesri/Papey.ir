using System.Net;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Papey.Printer;

internal sealed class PrinterForm : Form
{
    private readonly Settings _settings;
    private readonly PrintingApi _api;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly bool _test, _preview;
    private bool _busy, _paused, _exit;
    private DateTime _lastError;
    private string _status = "Starting";

    public PrinterForm(Settings settings, bool test, bool preview)
    {
        _settings = settings; _api = new(settings); _test = test; _preview = preview;
        Text = "Papey Order Printer"; Width = 440; Height = 700;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Controls.Add(_web);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status / وضعیت", null, (_, _) => MessageBox.Show(_status + "\n" + settings.Server + "\n" + settings.Printer, Text));
        menu.Items.Add("Pause / resume", null, (_, _) => { _paused = !_paused; Report(_paused ? "Paused" : "Resumed"); });
        menu.Items.Add("Resolve uncertain print…", null, async (_, _) => await Resolve());
        menu.Items.Add("Open log folder", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", Store.Folder));
        menu.Items.Add("Exit", null, (_, _) =>
        {
            if (_busy) { MessageBox.Show("A print job is in progress. Pause and wait for it to finish first.", Text); return; }
            _exit = true; Close();
        });
        _tray = new() { Icon = SystemIcons.Application, Text = "Papey Order Printer", ContextMenuStrip = menu, Visible = !test && !preview };
        Shown += async (_, _) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Store.PathFor("WebView"));
                await _web.EnsureCoreWebView2Async(env);
                Report("Receipt renderer initialized");
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                _web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                _web.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    if (_test || _preview) return; // Synthetic local test document, never a queue receipt.
                    if (e.Uri == "about:blank") return;
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
                        uri.GetLeftPart(UriPartial.Authority) != _api.Origin.GetLeftPart(UriPartial.Authority)) e.Cancel = true;
                };
                if (_test || _preview) { await TestReceipt(); _exit = true; Close(); return; }
                _timer.Tick += async (_, _) => await Tick();
                _timer.Start(); await Tick();
            }
            catch (Exception ex) { Report("Initialization failed: " + ex.GetType().Name); if (test || preview) { Environment.ExitCode = 1; _exit = true; Close(); } }
        };
        FormClosing += (_, e) => { if (!_exit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { _timer.Dispose(); _tray.Dispose(); _api.Dispose(); };
    }

    private void Report(string message)
    {
        _status = message; Store.Status(message);
        _tray.Text = ("Papey: " + message)[..Math.Min(63, message.Length + 7)];
    }

    private async Task Tick()
    {
        if (_busy || _paused) return;
        _busy = true;
        try
        {
            await _api.Connect();
            var journal = Store.Read<Journal>("pending.json");
            if (journal is not null)
            {
                if (JournalPolicy.Decide(journal) == Recovery.Acknowledge)
                {
                    PrinterHealth.EnsureReady(_settings.Printer);
                    if (!await _api.Action(journal.Lease, "complete")) { Review(journal); return; }
                    Store.Write<Journal?>("pending.json", null); Report("Printed job acknowledged");
                }
                else if (JournalPolicy.Decide(journal) == Recovery.ReleasePreparation)
                {
                    // No print call started; abandoning this preparation is safe.
                    await _api.Action(journal.Lease, "release"); Store.Write<Journal?>("pending.json", null);
                }
                else { Review(journal); return; }
            }
            PrinterHealth.EnsureReady(_settings.Printer);
            var lease = await _api.Claim();
            if (lease is null) { if (_status != "Connected — waiting for paid orders") Report("Connected — waiting for paid orders"); return; }
            Store.Write("pending.json", new Journal(lease, "Prepared"));
            await PrintOrder(lease);
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - _lastError > TimeSpan.FromMinutes(1))
            { _lastError = DateTime.UtcNow; Report("Connection / printing retry: " + ex.GetType().Name); }
        }
        finally { _busy = false; }
    }

    private void Review(Journal journal)
    {
        if (journal.State != "NeedsReview") Store.Write("pending.json", journal with { State = "NeedsReview" });
        _paused = true; Report("Paused: check paper before resolving an uncertain print");
        _tray.ShowBalloonTip(10000, Text, "A print result is uncertain. Check the paper, then choose Resolve uncertain print from this icon.", ToolTipIcon.Warning);
    }

    private async Task Resolve()
    {
        if (_busy) return;
        var journal = Store.Read<Journal>("pending.json");
        if (journal is null) { MessageBox.Show("No uncertain job.", Text); return; }
        var answer = MessageBox.Show("Did this order print?\nYes: acknowledge without printing again.\nNo: retry (only if you checked the paper and spooler).\nCancel: keep paused.", Text, MessageBoxButtons.YesNoCancel);
        if (answer == DialogResult.Cancel) return;
        _busy = true;
        try
        {
            if (!await _api.Action(journal.Lease, answer == DialogResult.Yes ? "complete" : "release"))
                throw new InvalidOperationException("The job was reclaimed. Check the server before retrying.");
            Store.Write<Journal?>("pending.json", null); _paused = false; Report("Print uncertainty resolved");
        }
        catch { MessageBox.Show("Could not resolve this job. Keep paused and check the server connection or queue.", Text); }
        finally { _busy = false; }
    }

    private async Task PrintOrder(Lease lease)
    {
        using var renewal = new CancellationTokenSource();
        var leaseValid = true;
        async Task RenewLoop()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(25), renewal.Token);
                    if (!await _api.Action(lease, "renew")) { leaseValid = false; return; }
                }
            }
            catch (OperationCanceledException) { }
            catch { leaseValid = false; }
        }
        var renewTask = RenewLoop();
        try
        {
            foreach (Cookie cookie in _api.Cookies.GetCookies(_api.Origin))
            {
                var copy = _web.CoreWebView2.CookieManager.CreateCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
                copy.IsHttpOnly = cookie.HttpOnly; copy.IsSecure = cookie.Secure;
                _web.CoreWebView2.CookieManager.AddOrUpdateCookie(copy);
            }
            await Navigate(_api.Receipt(lease).AbsoluteUri);
            await Ready(lease.OrderId);
            var printSettings = await PrintSettings();
            PrinterHealth.EnsureReady(_settings.Printer);
            if (!leaseValid || !await _api.Action(lease, "renew")) throw new InvalidOperationException("Print lease lost before submission.");
            Store.Write("pending.json", new Journal(lease, "Submitting"));
            Report("Sending paid order to BIXOLON");
            var result = await _web.CoreWebView2.PrintAsync(printSettings);
            if (result == CoreWebView2PrintStatus.PrinterUnavailable)
            {
                Store.Write("pending.json", new Journal(lease, "Prepared"));
                Report("Printer unavailable — queued for retry"); return;
            }
            if (result != CoreWebView2PrintStatus.Succeeded) { Review(new(lease, "NeedsReview")); return; }
            Store.Write("pending.json", new Journal(lease, "Submitted"));
            PrinterHealth.EnsureReady(_settings.Printer);
            if (!await _api.Action(lease, "complete")) { Review(new(lease, "NeedsReview")); return; }
            Store.Write<Journal?>("pending.json", null); Report("Paid order submitted to printer and acknowledged");
        }
        finally { renewal.Cancel(); await renewTask; }
    }

    private async Task Navigate(string uri)
    {
        var done = new TaskCompletionSource<bool>();
        void Finished(object? sender, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
        _web.CoreWebView2.NavigationCompleted += Finished;
        try
        {
            _web.CoreWebView2.Navigate(uri);
            if (!await done.Task.WaitAsync(TimeSpan.FromSeconds(40))) throw new IOException("Receipt navigation failed.");
        }
        finally { _web.CoreWebView2.NavigationCompleted -= Finished; }
    }

    private async Task Ready(int orderId)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var ready = await _web.CoreWebView2.ExecuteScriptAsync($$"""
                (() => { const r = document.querySelector('.receipt[data-print-ready="true"][data-order-id="{{orderId}}"]');
                return !!r && document.fonts.status === 'loaded' && [...r.querySelectorAll('img')].every(i => i.complete && i.naturalWidth > 0); })()
                """);
            if (ready == "true") return;
            await Task.Delay(250);
        }
        throw new TimeoutException("Receipt did not become print-ready.");
    }

    private async Task<CoreWebView2PrintSettings> PrintSettings()
    {
        var heightJson = await _web.CoreWebView2.ExecuteScriptAsync("Math.ceil(document.querySelector('.receipt').getBoundingClientRect().height)");
        var height = JsonSerializer.Deserialize<double>(heightJson);
        if (height < 1 || height > 18000) throw new InvalidOperationException("Unexpected receipt dimensions.");
        var settings = _web.CoreWebView2.Environment.CreatePrintSettings();
        settings.PrinterName = _settings.Printer;
        settings.PageWidth = 80 / 25.4;
        settings.PageHeight = Math.Max(100 / 25.4, height / 96 + 12 / 25.4);
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 4 / 25.4;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintBackgrounds = true;
        settings.ScaleFactor = 1; settings.Copies = 1;
        return settings;
    }

    private async Task TestReceipt()
    {
        var done = new TaskCompletionSource();
        void Finished(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();
        _web.CoreWebView2.NavigationCompleted += Finished;
        try
        {
            _web.CoreWebView2.NavigateToString("""
                <!doctype html><html lang="fa" dir="rtl"><meta charset="utf-8"><style>
                @page {size:80mm auto;margin:4mm}body{margin:0;background:white;font:14px Tahoma}
                .receipt{width:72mm;text-align:center;line-height:2;color:black}h1{font-size:24px}hr{border:0;border-top:1px dashed black}
                </style><div class="receipt" data-print-ready="true" data-order-id="0"><h1>پاپی</h1><hr>
                چاپ آزمایشی اتصال خودکار سفارش‌ها<br>تیشرت — مشکی — سایز XL<br>تعداد: ۲ | مبلغ: ۲٬۲۸۰٬۰۰۰ تومان<hr>
                <div dir="ltr">Papey.ir • BIXOLON SRP-330II</div>این رسید سفارش واقعی نیست.</div></html>
                """);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Report("Test receipt loaded");
            await Ready(0);
            var settings = await PrintSettings();
            if (_preview)
            {
                if (!await _web.CoreWebView2.PrintToPdfAsync(Store.PathFor("test-receipt.pdf"), settings)) throw new IOException("PDF preview failed.");
                Report("Test receipt PDF generated; no paper printed");
            }
            else
            {
                var result = await _web.CoreWebView2.PrintAsync(settings);
                if (result != CoreWebView2PrintStatus.Succeeded) throw new IOException("Test print failed: " + result);
                Report("Test receipt accepted by BIXOLON printer spooler");
            }
        }
        finally { _web.CoreWebView2.NavigationCompleted -= Finished; }
    }
}
