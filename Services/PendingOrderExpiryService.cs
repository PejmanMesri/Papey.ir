using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

/// <summary>
/// Background sweep: pending orders that were never confirmed by the payment
/// gateway become PaymentFailed after 20 minutes. Runs every minute.
/// </summary>
public sealed class PendingOrderExpiryService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<PendingOrderExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await ExpireAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pending-order expiry sweep failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - Expiry;
        var expired = await db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt <= cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.PaymentFailed), ct);
        if (expired > 0)
            logger.LogInformation("Expired {Count} pending order(s) older than {Minutes} min → PaymentFailed", expired, Expiry.TotalMinutes);
    }
}
