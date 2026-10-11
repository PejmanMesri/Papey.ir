using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed record PrintJobLease(Guid JobId, int OrderId, string OrderNumber,
    string OrderStatus, DateTime? PaidAt, string ReceiptUrl, Guid LeaseToken,
    DateTime LeaseExpiresAt, int Attempts);

public sealed class OrderPrintQueue(IDbContextFactory<AppDbContext> dbFactory)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public static IQueryable<OrderPrintJob> ReadyJobs(AppDbContext db, DateTime now) =>
        db.OrderPrintJobs.Where(j => j.PrintedAt == null && j.AvailableAt <= now &&
            (j.LeaseExpiresAt == null || j.LeaseExpiresAt <= now) &&
            j.Order.PaidAt != null &&
            (j.Order.Status == OrderStatus.Paid || j.Order.Status == OrderStatus.Processing ||
             j.Order.Status == OrderStatus.Shipped || j.Order.Status == OrderStatus.Delivered));

    public async Task<PrintJobLease?> ClaimAsync(int userId, string clientId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        // Lock only the selected job. Concurrent desktop clients skip locked rows.
        var jobs = await db.OrderPrintJobs.FromSqlInterpolated($"""
            SELECT j.* FROM "OrderPrintJobs" j
            JOIN "Orders" o ON o."Id" = j."OrderId"
            WHERE j."PrintedAt" IS NULL AND j."AvailableAt" <= {now}
              AND (j."LeaseExpiresAt" IS NULL OR j."LeaseExpiresAt" <= {now})
              AND o."PaidAt" IS NOT NULL
              AND o."Status" IN ({OrderStatus.Paid}, {OrderStatus.Processing}, {OrderStatus.Shipped}, {OrderStatus.Delivered})
            ORDER BY j."CreatedAt", j."Id"
            LIMIT 1 FOR UPDATE OF j SKIP LOCKED
            """).ToListAsync(ct);
        var job = jobs.FirstOrDefault();
        if (job is null) return null;

        job.LeaseToken = Guid.NewGuid();
        job.LeaseExpiresAt = now + LeaseDuration;
        job.ClaimedByUserId = userId;
        job.ClientId = clientId;
        job.Attempts++;
        await db.SaveChangesAsync(ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == job.OrderId, ct);
        await transaction.CommitAsync(ct);
        return new(job.Id, order.Id, order.OrderNumber, order.Status.ToString(), order.PaidAt,
            $"/admin/print/order/{order.Id}?autoPrint=false", job.LeaseToken.Value,
            job.LeaseExpiresAt.Value, job.Attempts);
    }

    private static IQueryable<OrderPrintJob> OwnedJob(AppDbContext db, Guid id, int userId,
        string clientId, Guid leaseToken) => db.OrderPrintJobs.Where(j => j.Id == id &&
            j.ClaimedByUserId == userId && j.ClientId == clientId && j.LeaseToken == leaseToken);

    public async Task<DateTime?> RenewAsync(Guid id, int userId, string clientId, Guid token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var expires = now + LeaseDuration;
        var updated = await OwnedJob(db, id, userId, clientId, token)
            .Where(j => j.PrintedAt == null && j.LeaseExpiresAt > now && j.Order.PaidAt != null &&
                j.Order.Status != OrderStatus.Cancelled && j.Order.Status != OrderStatus.PaymentFailed &&
                j.Order.Status != OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, expires), ct);
        return updated == 1 ? expires : null;
    }

    public async Task<bool> CompleteAsync(Guid id, int userId, string clientId, Guid token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var owned = OwnedJob(db, id, userId, clientId, token);
        // A late acknowledgement is safe if no other client has reclaimed the job.
        var updated = await owned.Where(j => j.PrintedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.PrintedAt, DateTime.UtcNow)
                .SetProperty(j => j.LastError, (string?)null), ct);
        return updated == 1 || await owned.AnyAsync(j => j.PrintedAt != null, ct);
    }

    public async Task<bool> ReleaseAsync(Guid id, int userId, string clientId, Guid token, string? error, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var retryAt = DateTime.UtcNow.AddSeconds(15);
        return await OwnedJob(db, id, userId, clientId, token).Where(j => j.PrintedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseToken, (Guid?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.ClaimedByUserId, (int?)null)
                .SetProperty(j => j.ClientId, (string?)null)
                .SetProperty(j => j.LastError, error)
                .SetProperty(j => j.AvailableAt, retryAt), ct) == 1;
    }
}
