using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public ZarinpalService(IConfiguration config, ILogger<ZarinpalService> logger)
    {
        _logger = logger;
        _opt = new ZarinpalOptions
        {
            MerchantId = config["ZARINPAL_MERCHANT_ID"] ?? "",
            Sandbox = (config["ZARINPAL_SANDBOX"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase),
            CallbackUrl = config["ZARINPAL_CALLBACK_URL"] ?? ""
        };
        var baseUrl = _opt.Sandbox ? "https://sandbox.zarinpal.com/" : "https://payment.zarinpal.com/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string GatewayBase => _opt.Sandbox ? "https://sandbox.zarinpal.com" : "https://payment.zarinpal.com";

    public async Task<PaymentRequestResult> RequestPaymentAsync(long amountRials, string description, string? mobile = null, string? email = null)
    {
        try
        {
            var payload = new
            {
                merchant_id = _opt.MerchantId,
                amount = amountRials,
                callback_url = _opt.CallbackUrl,
                description,
                metadata = new { mobile, email }
            };
            var res = await _http.PostAsync("pg/v4/payment/request.json",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            var code = data.GetProperty("code").GetInt32();
            if (code == 100)
            {
                var authority = data.GetProperty("authority").GetString()!;
                return new PaymentRequestResult(true, authority, $"{GatewayBase}/pg/StartPay/{authority}", null);
            }
            var errors = doc.RootElement.GetProperty("errors");
            return new PaymentRequestResult(false, null, null, $"Zarinpal error code {code}: {errors}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zarinpal payment request failed");
            return new PaymentRequestResult(false, null, null, ex.Message);
        }
    }

    public async Task<PaymentVerifyResult> VerifyPaymentAsync(string authority, long amountRials)
    {
        try
        {
            var payload = new { merchant_id = _opt.MerchantId, amount = amountRials, authority };
            var res = await _http.PostAsync("pg/v4/payment/verify.json",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            var code = data.GetProperty("code").GetInt32();
            if (code is 100 or 101)
            {
                var refId = data.GetProperty("ref_id").GetInt64();
                return new PaymentVerifyResult(true, refId, null);
            }
            return new PaymentVerifyResult(false, 0, $"Zarinpal verify error code {code}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zarinpal verify failed");
            return new PaymentVerifyResult(false, 0, ex.Message);
        }
    }
}
