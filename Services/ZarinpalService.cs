using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Papey.Services;

public class ZarinpalOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public bool Sandbox { get; set; } = true;
    public string CallbackUrl { get; set; } = string.Empty;
}

public record PaymentRequestResult(bool Success, string? Authority, string? PaymentUrl, string? Error);
public record PaymentVerifyResult(bool Success, long RefId, string? Error);

public class ZarinpalService
{
    private readonly HttpClient _http;
    private readonly ZarinpalOptions _opt;
    private readonly ILogger<ZarinpalService> _logger;
    public ZarinpalService(IConfiguration config, ILogger<ZarinpalService> logger, HttpClient? http = null)
    {
        _logger = logger;
        var sandbox = !bool.TryParse(config["ZARINPAL_SANDBOX"], out var parsed) || parsed;
        var origin = config["SITE_URL"] ?? config["RENDER_EXTERNAL_URL"];
        _opt = new() {
            MerchantId = config["ZARINPAL_MERCHANT_ID"]?.Trim() ?? "", Sandbox = sandbox,
            CallbackUrl = config["ZARINPAL_CALLBACK_URL"] ?? (string.IsNullOrWhiteSpace(origin) ? "" : origin.TrimEnd('/') + "/payment/callback")
        };
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(20);
    }
    public string GatewayBase => _opt.Sandbox ? "https://sandbox.zarinpal.com" : "https://payment.zarinpal.com";
    public bool IsConfigured => Guid.TryParse(_opt.MerchantId, out var id) && id != Guid.Empty
        && Uri.TryCreate(_opt.CallbackUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (_opt.Sandbox && uri.IsLoopback && uri.Scheme == "http"));
    public bool IsSandbox => _opt.Sandbox;
    public static bool ValidAuthority(string? authority) => authority is not null && Regex.IsMatch(authority, "^[A-Za-z0-9]{32,64}$");
    public static long ToRials(decimal toman)
    {
        var rials = checked(toman * 10m);
        if (rials <= 0 || rials != decimal.Truncate(rials) || rials > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(toman));
        return (long)rials;
    }
    private static bool Data(JsonElement root, out JsonElement data, out int code)
    {
        code = 0;
        return root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("code", out var value) && value.TryGetInt32(out code);
    }
    private static string Error(JsonElement root, string operation)
    {
        int? code = null;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
            && errors.TryGetProperty("code", out var value) && value.TryGetInt32(out var parsed)) code = parsed;
        else if (Data(root, out _, out var dataCode)) code = dataCode;
        return code is int number ? $"زرین‌پال {operation} را نپذیرفت (کد {number})." : "پاسخ درگاه پرداخت معتبر نیست؛ دوباره تلاش کنید.";
    }
    public async Task<PaymentRequestResult> RequestPaymentAsync(long amountRials, string description, string? mobile = null, string? email = null)
    {
        if (!IsConfigured) return new(false, null, null, "درگاه پرداخت هنوز فعال نشده است. با پشتیبانی تماس بگیرید.");
        if (amountRials <= 0 || string.IsNullOrWhiteSpace(description)) return new(false, null, null, "مبلغ یا مشخصات پرداخت معتبر نیست.");
        try
        {
            var metadata = new Dictionary<string, string>();
            if (SmsService.NormalizePhone(mobile) is string normalized) metadata["mobile"] = normalized;
            if (!string.IsNullOrWhiteSpace(email)) metadata["email"] = email;
            var payload = new { merchant_id = _opt.MerchantId, amount = amountRials, currency = "IRR", callback_url = _opt.CallbackUrl, description, metadata };
            using var res = await _http.PostAsync(GatewayBase + "/pg/v4/payment/request.json", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (res.IsSuccessStatusCode && Data(doc.RootElement, out var data, out var code) && code == 100
                && data.TryGetProperty("authority", out var a) && a.ValueKind == JsonValueKind.String && ValidAuthority(a.GetString()))
            {
                var authority = a.GetString()!;
                return new(true, authority, GatewayBase + "/pg/StartPay/" + authority, null);
            }
            return new(false, null, null, Error(doc.RootElement, "درخواست پرداخت"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Zarinpal request failed ({Failure})", ex.GetType().Name);
            return new(false, null, null, "ارتباط با درگاه پرداخت برقرار نشد؛ کمی بعد دوباره تلاش کنید.");
        }
    }
    public async Task<PaymentVerifyResult> VerifyPaymentAsync(string authority, long amountRials)
    {
        if (!IsConfigured || !ValidAuthority(authority) || amountRials <= 0) return new(false, 0, "مشخصات تأیید پرداخت معتبر نیست.");
        try
        {
            var payload = new { merchant_id = _opt.MerchantId, amount = amountRials, authority };
            using var res = await _http.PostAsync(GatewayBase + "/pg/v4/payment/verify.json", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (res.IsSuccessStatusCode && Data(doc.RootElement, out var data, out var code) && code is 100 or 101
                && data.TryGetProperty("ref_id", out var value) && value.TryGetInt64(out var refId) && refId > 0) return new(true, refId, null);
            return new(false, 0, Error(doc.RootElement, "تأیید پرداخت"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Zarinpal verify failed ({Failure})", ex.GetType().Name);
            return new(false, 0, "ارتباط برای تأیید پرداخت برقرار نشد. اگر مبلغ کسر شده، کد سفارش را به پشتیبانی اعلام کنید.");
        }
    }
}
