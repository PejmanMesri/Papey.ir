using System.Net;
using System.Net.Http.Json;

namespace Papey.Printer;

internal sealed class PrintingApi(Settings settings, HttpMessageHandler? transport = null) : IDisposable
{
    public readonly CookieContainer Cookies = new();
    private HttpClient? _http;
    private string? _csrf;
    private DateTime _nextLogin;
    public Uri Origin { get; } = new(settings.Server);
    private HttpClient Http => _http ??= new(transport ?? new HttpClientHandler { CookieContainer = Cookies, AllowAutoRedirect = false })
        { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(20) };

    public async Task Connect()
    {
        if (Origin.Scheme != "https" || Origin.AbsolutePath != "/" || !string.IsNullOrEmpty(Origin.UserInfo))
            throw new InvalidOperationException("Configure an HTTPS server origin.");
        if (_csrf is not null) return;
        if (DateTime.UtcNow < _nextLogin) throw new InvalidOperationException("Waiting before authentication retry.");
        _nextLogin = DateTime.UtcNow.AddMinutes(1);
        using var login = await Http.PostAsJsonAsync("/api/admin/printing/login",
            new { identifier = settings.Username, password = Store.Unprotect(settings.ProtectedPassword) });
        login.EnsureSuccessStatusCode();
        using var session = await Http.GetAsync("/api/admin/printing/session");
        session.EnsureSuccessStatusCode();
        var data = await session.Content.ReadFromJsonAsync<Session>();
        _csrf = data?.CsrfToken ?? throw new InvalidOperationException("Missing printing session.");
    }

    private sealed record Session(string CsrfToken);
    private async Task<HttpResponseMessage> Post(string path, object body)
    {
        await Connect();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/printing/" + path)
            { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        var response = await Http.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            _csrf = null;
        return response;
    }
    public async Task<Lease?> Claim()
    {
        using var response = await Post("jobs/claim", new { clientId = settings.ClientId });
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<Lease>();
    }
    public async Task<bool> Action(Lease lease, string action)
    {
        using var response = await Post($"jobs/{lease.JobId}/{action}", new { clientId = settings.ClientId, lease.LeaseToken });
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        response.EnsureSuccessStatusCode(); return true;
    }
    public Uri Receipt(Lease lease)
    {
        var uri = new Uri(Origin, lease.ReceiptUrl);
        if (uri.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority) ||
            uri.AbsolutePath != $"/admin/print/order/{lease.OrderId}" || uri.Query != "?autoPrint=false")
            throw new InvalidOperationException("Unexpected receipt address.");
        return uri;
    }
    public void Dispose() => _http?.Dispose();
}
