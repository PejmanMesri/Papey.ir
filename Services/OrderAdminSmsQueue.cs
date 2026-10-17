using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;
namespace Papey.Services;

public sealed class OrderAdminSmsQueue(IDbContextFactory<AppDbContext> factory, SmsService sms)
{
    // Called inside the payment transaction while the order row is locked.
    public async Task EnqueueAsync(AppDbContext db, Order order, CancellationToken ct = default)
    {
        if (order.PaidAt is null || order.Status != OrderStatus.Paid) return;
        foreach (var phone in sms.OrderNotificationPhones)
            if (!await db.OrderAdminSmsJobs.AnyAsync(j => j.OrderId == order.Id && j.Phone == phone, ct))
                db.OrderAdminSmsJobs.Add(new() { OrderId = order.Id, Phone = phone, IsSandbox = sms.IsSandbox });
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // A worker interrupted after sending may have reached the provider. Do not automatically resend it.
        await db.OrderAdminSmsJobs.Where(j => j.State == ShippingSmsState.Sending && j.StartedAt < DateTime.UtcNow.AddMinutes(-2))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, ShippingSmsState.Unknown)
                .SetProperty(j => j.Error, "ارسال قطع شده است؛ گزارش SMS.ir را بررسی کنید."), ct);
        if (!sms.CanSendOrderNotification) return false;
        OrderAdminSmsJob? job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.OrderAdminSmsJobs.FromSqlInterpolated($"""
                SELECT * FROM "OrderAdminSmsJobs" WHERE "State" = {(int)ShippingSmsState.Pending}
                    AND "AvailableAt" <= {DateTime.UtcNow} AND "IsSandbox" = {sms.IsSandbox}
                ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            job = rows.FirstOrDefault();
            if (job is null) return false;
            job.State = ShippingSmsState.Sending;
            job.StartedAt = DateTime.UtcNow;
            job.Attempts++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == job.OrderId, ct);
        if (order.PaidAt is null || order.Status is OrderStatus.Pending or OrderStatus.PaymentFailed or OrderStatus.Cancelled ||
            !sms.OrderNotificationPhones.Contains(job.Phone))
        {
            job.State = ShippingSmsState.Cancelled;
        }
        else
        {
            var result = await sms.SendOrderNotificationAsync(job.Phone, order.OrderNumber, ct);
            job.Error = result.Error;
            job.MessageId = result.MessageId;
            if (result.Success)
            {
                job.State = ShippingSmsState.Accepted;
                job.AcceptedAt = DateTime.UtcNow;
            }
            else if (result.Uncertain) job.State = ShippingSmsState.Unknown;
            else if (job.Attempts >= 3) job.State = ShippingSmsState.Failed;
            else
            {
                job.State = ShippingSmsState.Pending;
                job.AvailableAt = DateTime.UtcNow.AddMinutes(job.Attempts * 2);
            }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class OrderAdminSmsWorker(IServiceScopeFactory scopes, ILogger<OrderAdminSmsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<OrderAdminSmsQueue>().ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Order admin SMS worker failed ({Failure})", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
