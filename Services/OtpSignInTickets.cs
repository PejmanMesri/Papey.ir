using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Papey.Services;

/// <summary>Single-use handoff from a verified Blazor OTP flow to an HTTP cookie response.
/// Runs on the single application instance; a restart safely invalidates pending tickets.</summary>
public sealed class OtpSignInTickets(TimeProvider clock)
{
    private sealed record Ticket(int UserId, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new();

    public string Create(int userId)
    {
        var now = clock.GetUtcNow();
        foreach (var pair in _tickets)
            if (pair.Value.ExpiresAt <= now) _tickets.TryRemove(pair.Key, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[token] = new Ticket(userId, now.AddMinutes(1));
        return token;
    }

    public int? Consume(string? token)
    {
        if (token is null || token.Length != 64 || !_tickets.TryRemove(token, out var ticket))
            return null;
        return ticket.ExpiresAt > clock.GetUtcNow() ? ticket.UserId : null;
    }
}
