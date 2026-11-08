using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed class CustomDesignSmsQueue(IDbContextFactory<AppDbContext> factory, SmsService sms)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CustomDesignSmsJobs.Where(j => j.State == ShippingSmsState.Sending && j.StartedAt < DateTime.UtcNow.AddMinutes(-2))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, ShippingSmsState.Unknown)
                .SetProperty(j => j.Error, "نتیجه ارسال مشخص نیست؛ گزارش SMS.ir را بررسی کنید."), ct);
        if (!sms.CanSendCustomDesign) return false;
        CustomDesignSmsJob? job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            job = (await db.CustomDesignSmsJobs.FromSqlInterpolated($"""
                SELECT * FROM "CustomDesignSmsJobs" WHERE "State" = {(int)ShippingSmsState.Pending}
                AND "AvailableAt" <= {DateTime.UtcNow} AND "IsSandbox" = {sms.IsSandbox}
                ORDER BY "AvailableAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct)).FirstOrDefault();
            if (job is null) return false;
            job.State = ShippingSmsState.Sending; job.StartedAt = DateTime.UtcNow; job.Attempts++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        if (!sms.CustomNotificationPhones.Contains(job.Phone)) job.State = ShippingSmsState.Cancelled;
        else
        {
            var number = job.CallbackRequestId is Guid callbackId
                ? (await db.CustomCallbackRequests.AsNoTracking().SingleAsync(r => r.Id == callbackId, ct)).Number
                : (await db.CustomDesignRequests.AsNoTracking().SingleAsync(r => r.Id == job.RequestId, ct)).Number;
            var result = await sms.SendCustomDesignAsync(job.Phone, number, ct);
            job.MessageId = result.MessageId; job.Error = result.Error;
            job.State = result.Success ? ShippingSmsState.Accepted : result.Uncertain ? ShippingSmsState.Unknown
                : job.Attempts >= 3 ? ShippingSmsState.Failed : ShippingSmsState.Pending;
            if (job.State == ShippingSmsState.Pending) job.AvailableAt = DateTime.UtcNow.AddMinutes(job.Attempts * 2);
        }
        await db.SaveChangesAsync(ct); return true;
    }
}

public sealed class CustomDesignSmsWorker(IServiceScopeFactory scopes, ILogger<CustomDesignSmsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<CustomDesignSmsQueue>().ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Custom design SMS worker failed ({Failure})", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
