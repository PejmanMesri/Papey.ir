using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Hubs;

namespace Papey.Services;

/// <summary>Notifications are hints; the database queue is the source of truth.</summary>
public sealed class PrintJobNotifier(IDbContextFactory<AppDbContext> dbFactory,
    IHubContext<PrintingHub> hub, ILogger<PrintJobNotifier> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    if (await OrderPrintQueue.ReadyJobs(db, DateTime.UtcNow).AnyAsync(stoppingToken))
                        await hub.Clients.All.SendAsync("PrintJobsAvailable", cancellationToken: stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not notify desktop printers; jobs remain queued");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
