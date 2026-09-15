using System.Text;
using System.Text.Json;

namespace Papey.Services;

public class SmsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SmsService> _logger;

    public SmsService(IConfiguration config, ILogger<SmsService> logger)
    {
        _config = config;
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri("https://api.kavenegar.com/") };
    }

    public string[] AdminPhones =>
        (_config["ADMIN_PHONES"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task SendToAdminsAsync(string message)
    {
        foreach (var phone in AdminPhones)
            await SendSmsAsync(phone, message);
    }

    public async Task SendSmsAsync(string receptor, string message)
    {
        var apiKey = _config["KAVENEGAR_API_KEY"] ?? "";
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            _logger.LogWarning("KaveNegar API key not configured. SMS to {Receptor} skipped: {Message}", receptor, message);
            return;
        }
        try
        {
            var sender = _config["KAVENEGAR_SENDER"] ?? "";
            var url = $"v1/{apiKey}/sms/send.json?receptor={Uri.EscapeDataString(receptor)}&message={Uri.EscapeDataString(message)}";
            if (!string.IsNullOrWhiteSpace(sender))
                url += $"&sender={Uri.EscapeDataString(sender)}";
            var res = await _http.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            _logger.LogInformation("KaveNegar SMS to {Receptor}: {Status} {Body}", receptor, res.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {Receptor}", receptor);
        }
    }
}
