using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Papey.Services;

public sealed record SmsSendResult(bool Success, string? Error = null, long? MessageId = null,
    int Accepted = 0, int Rejected = 0, bool Uncertain = false, string? PackId = null);

public sealed class SmsService(HttpClient http, IConfiguration config, IHostEnvironment environment,
    ILogger<SmsService> logger)
{
    public bool IsSandbox => environment.IsDevelopment() || config.GetValue<bool>("SMSIR_SANDBOX");
    private string ApiKey => config[IsSandbox ? "SMSIR_SANDBOX_API_KEY" : "SMSIR_API_KEY"] ?? "";
    public int OtpTemplateId => IsSandbox ? config.GetValue<int?>("SMSIR_SANDBOX_TEMPLATE_ID") ?? 123456 : config.GetValue<int?>("SMSIR_OTP_TEMPLATE_ID") ?? 0;
    public int ShippingTemplateId => config.GetValue<int?>("SMSIR_SHIPPING_TEMPLATE_ID") ?? 0;
    public int ShippingTrackingTemplateId => config.GetValue<int?>("SMSIR_SHIPPING_TRACKING_TEMPLATE_ID") ?? 0;
    public long LineNumber => config.GetValue<long?>("SMSIR_LINE_NUMBER") ?? 0;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith("YOUR_");
    public bool CanSendOtp => IsConfigured && (OtpTemplateId > 0 || (!IsSandbox && LineNumber > 0)) && (environment.IsDevelopment() || !IsSandbox);
    public bool CanSendBulk => IsConfigured && LineNumber > 0;
    public bool CanSendShipping => IsConfigured && (LineNumber > 0 || (!IsSandbox && (ShippingTemplateId > 0 || ShippingTrackingTemplateId > 0)));
    public string[] OrderNotificationPhones => (config["ORDER_NOTIFICATION_PHONES"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizePhone).OfType<string>().Distinct().ToArray();
    public int OrderTemplateId => config.GetValue<int?>("SMSIR_ORDER_TEMPLATE_ID") ?? 0;
    public bool CanSendOrderNotification => IsConfigured && (LineNumber > 0 || (!IsSandbox && OrderTemplateId > 0));
    public int CustomDesignTemplateId => config.GetValue<int?>("SMSIR_CUSTOM_DESIGN_TEMPLATE_ID") ?? 0;
    public bool CanSendCustomDesign => IsConfigured && (LineNumber > 0 || (!IsSandbox && CustomDesignTemplateId > 0));
    public Task<SmsSendResult> SendCustomDesignAsync(string phone, string number, CancellationToken ct = default) =>
        !IsSandbox && CustomDesignTemplateId > 0
            ? SendVerifyAsync(phone, CustomDesignTemplateId, "REQUESTNUMBER", number, ct)
            : SendBulkAsync([phone], $"پاپی؛ درخواست طرح اختصاصی جدید {number} ثبت شد. برای بررسی طرح و تماس با مشتری: https://papey.ir/admin/custom-designs", ct);

    public static string? NormalizePhone(string? phone)
    {
        var digits = PersianHelpers.ToEnglishDigits(phone ?? "").Trim().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (digits.StartsWith("+98")) digits = "0" + digits[3..];
        else if (digits.StartsWith("0098")) digits = "0" + digits[4..];
        else if (digits.StartsWith("98") && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.StartsWith('9') && digits.Length == 10) digits = "0" + digits;
        return Regex.IsMatch(digits, @"^09[0-9]{9}$") ? digits : null;
    }

    public Task<SmsSendResult> SendOtpAsync(string phone, string code, CancellationToken ct = default) =>
        OtpTemplateId > 0 ? SendVerifyAsync(phone, OtpTemplateId, "CODE", code, ct)
        : SendBulkAsync([phone], $"پاپی؛ کد ورود شما: {code}", ct);

    public Task<SmsSendResult> SendShippingAsync(string phone, string orderNumber, CancellationToken ct = default) =>
        IsSandbox || ShippingTemplateId <= 0 ? SendBulkAsync([phone], $"پاپی؛ سفارش {orderNumber} شما ارسال شد.", ct)
        : SendVerifyAsync(phone, ShippingTemplateId, "ORDERNUMBER", orderNumber, ct);

    public Task<SmsSendResult> SendShippingAsync(string phone, string orderNumber,
        string? courier, string? trackingNumber, CancellationToken ct = default)
    {
        if (!ShipmentTracking.IsValid(courier, trackingNumber)) return SendShippingAsync(phone, orderNumber, ct);
        var number = ShipmentTracking.NormalizeNumber(trackingNumber);
        if (IsSandbox || ShippingTrackingTemplateId <= 0)
            return SendBulkAsync([phone], $"پاپی؛ سفارش {orderNumber} شما ارسال شد.\nشرکت ارسال: {courier!.Trim()}\nکد رهگیری مرسوله: {number}\nجزئیات سفارش: https://papey.ir/account/orders", ct);
        var mobile = NormalizePhone(phone);
        if (mobile is null || orderNumber.Length is 0 or > 25)
            return Task.FromResult(new SmsSendResult(false, "شماره یا مشخصات مرسوله معتبر نیست."));
        return SendAsync("send/verify", new { mobile, templateId = ShippingTrackingTemplateId, parameters = new[] {
            new { name = "ORDERNUMBER", value = orderNumber },
            new { name = "COURIER", value = courier!.Trim() },
            new { name = "TRACKINGCODE", value = number }
        } }, 1, false, ct);
    }

    public Task<SmsSendResult> SendOrderNotificationAsync(string phone, string orderNumber, CancellationToken ct = default) =>
        !IsSandbox && OrderTemplateId > 0
            ? SendVerifyAsync(phone, OrderTemplateId, "ORDERNUMBER", orderNumber, ct)
            : SendBulkAsync([phone], $"پاپی؛ سفارش پرداخت‌شده جدید به شماره {orderNumber} ثبت شد. برای مشاهده جزئیات به papey.ir/admin مراجعه کنید.", ct);

    public Task<SmsSendResult> SendVerifyAsync(string phone, int templateId, string name, string value, CancellationToken ct = default)
    {
        var mobile = NormalizePhone(phone);
        if (mobile is null || templateId <= 0 || string.IsNullOrWhiteSpace(name) || value.Length is 0 or > 25)
            return Task.FromResult(new SmsSendResult(false, "شماره یا قالب پیامک معتبر نیست."));
        return SendAsync("send/verify", new { mobile, templateId, parameters = new[] { new { name, value } } }, 1, false, ct);
    }

    public Task<SmsSendResult> SendBulkAsync(IEnumerable<string> phones, string message, CancellationToken ct = default)
    {
        var normalized = phones.Select(NormalizePhone).ToArray();
        if (normalized.Any(p => p is null)) return Task.FromResult(new SmsSendResult(false, "شماره موبایل نامعتبر است."));
        var mobiles = normalized.Cast<string>().Distinct().ToArray();
        if (mobiles.Length is 0 or > 100 || string.IsNullOrWhiteSpace(message) || message.Length > 1000 || !CanSendBulk)
            return Task.FromResult(new SmsSendResult(false, "خط ارسال، متن یا تعداد گیرندگان معتبر نیست (حداکثر ۱۰۰ شماره و ۱۰۰۰ کاراکتر)."));
        return SendAsync("send/bulk", new { lineNumber = LineNumber, messageText = message, mobiles, sendDateTime = (long?)null }, mobiles.Length, true, ct);
    }

    private async Task<SmsSendResult> SendAsync(string path, object payload, int count, bool bulk, CancellationToken ct)
    {
        if (!IsConfigured) return new(false, "سرویس پیامک تنظیم نشده است.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sms.ir/v1/" + path);
            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Accept.ParseAdd("application/json");
            request.Content = JsonContent.Create(payload);
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                return new(false, "نتیجه ارسال مشخص نیست؛ پیش از ارسال مجدد گزارش سرویس را بررسی کنید.", Uncertain: true);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !body.RootElement.TryGetProperty("status", out var status) || status.GetInt32() != 1)
            {
                // Never log keys, OTPs, phone numbers, or provider response bodies.
                logger.LogWarning("SMS.ir rejected {Operation}: HTTP {Status}", path, (int)response.StatusCode);
                return new(false, "سرویس پیامک درخواست را نپذیرفت. تنظیمات قالب و اعتبار را بررسی کنید.", Rejected: count);
            }
            var data = body.RootElement.GetProperty("data");
            if (!bulk)
            {
                var id = data.GetProperty("messageId").GetInt64();
                return id > 0 ? new(true, MessageId: id, Accepted: 1) : new(false, "شناسه پیامک معتبر دریافت نشد.", Uncertain: true);
            }
            var ids = data.GetProperty("messageIds").EnumerateArray().ToArray();
            if (ids.Length != count) return new(false, "پاسخ ارسال ناقص است؛ پیش از ارسال مجدد گزارش سرویس را بررسی کنید.", Uncertain: true);
            var accepted = ids.Count(id => id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0);
            var pack = data.TryGetProperty("packId", out var packId) ? packId.GetString() : null;
            return new(accepted == count, accepted == count ? null : "بعضی شماره‌ها توسط سرویس پذیرفته نشدند (از جمله لیست سیاه).", MessageId: count == 1 && accepted == 1 ? ids[0].GetInt64() : null, Accepted: accepted, Rejected: count - accepted, PackId: pack);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            logger.LogWarning("SMS.ir {Operation} returned no conclusive response ({Failure})", path, ex.GetType().Name);
            return new(false, "نتیجه ارسال مشخص نیست؛ پیش از ارسال مجدد گزارش سرویس را بررسی کنید.", Uncertain: true);
        }
    }
}
