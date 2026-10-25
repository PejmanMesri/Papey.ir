using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;
namespace Papey.Services;

public sealed class ShippingSmsQueue(IDbContextFactory<AppDbContext> factory, SmsService sms)
{
    public async Task<bool> SaveTrackingAsync(int orderId, string courier, string number, CancellationToken ct = default)
    {
        if (!ShipmentTracking.IsValid(courier, number)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(ct);
        var order = rows.FirstOrDefault();
        if (order?.PaidAt is null || order.Status is OrderStatus.Pending or OrderStatus.PaymentFailed or OrderStatus.Cancelled
            || await db.ShippingSmsJobs.AnyAsync(j => j.OrderId == orderId && j.State == ShippingSmsState.Sending, ct)) return false;
        order.CourierName = courier.Trim();
        order.TrackingNumber = ShipmentTracking.NormalizeNumber(number);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SetOrderStatusAsync(int orderId, OrderStatus status, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status) || status is OrderStatus.Pending or OrderStatus.PaymentFailed) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var orders = await db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(ct);
        var order = orders.FirstOrDefault();
        if (order is null || order.Status is OrderStatus.Pending or OrderStatus.PaymentFailed) return false;
        if (status == OrderStatus.Shipped && !ShipmentTracking.IsComplete(order)) return false;
        if (status == OrderStatus.Shipped && order.Status != OrderStatus.Shipped &&
            !await db.ShippingSmsJobs.AnyAsync(j => j.OrderId == order.Id, ct))
            db.ShippingSmsJobs.Add(new() { OrderId = order.Id, IsSandbox = sms.IsSandbox });
        order.Status = status;
        if (status == OrderStatus.Shipped) order.ShippedAt ??= DateTime.UtcNow;
        if (status == OrderStatus.Paid && order.PaidAt is null) order.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ShippingSmsJobs.Where(j => j.Id == id && j.State == ShippingSmsState.Failed && j.IsSandbox == sms.IsSandbox &&
                (j.Order.Status == OrderStatus.Shipped || j.Order.Status == OrderStatus.Delivered))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, ShippingSmsState.Pending)
                .SetProperty(j => j.Attempts, 0).SetProperty(j => j.AvailableAt, DateTime.UtcNow).SetProperty(j => j.Error, (string?)null), ct) > 0;
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // A worker interrupted after sending may have reached the provider. Do not automatically resend it.
        await db.ShippingSmsJobs.Where(j => j.State == ShippingSmsState.Sending && j.StartedAt < DateTime.UtcNow.AddMinutes(-2))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, ShippingSmsState.Unknown)
                .SetProperty(j => j.Error, "ارسال قطع شده است؛ گزارش SMS.ir را بررسی کنید."), ct);
        if (!sms.CanSendShipping) return false;
        ShippingSmsJob? job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.ShippingSmsJobs.FromSqlInterpolated($"""
                SELECT * FROM "ShippingSmsJobs" WHERE "State" = {(int)ShippingSmsState.Pending}
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
        if (order.Status is not (OrderStatus.Shipped or OrderStatus.Delivered))
        {
            job.State = ShippingSmsState.Cancelled;
        }
        else
        {
            var result = await sms.SendShippingAsync(order.CustomerPhone, order.OrderNumber,
                order.CourierName, order.TrackingNumber, ct);
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

public sealed class ShippingSmsWorker(IServiceScopeFactory scopes, ILogger<ShippingSmsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<ShippingSmsQueue>().ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Shipping SMS worker failed ({Failure})", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
