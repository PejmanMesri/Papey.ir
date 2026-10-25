using Microsoft.EntityFrameworkCore;
using Papey.Data;
using Papey.Models;

namespace Papey.Services;

public sealed record ReviewInput(int PrintQuality, int FabricQuality, ReviewFit Fit, string Comment);
public sealed record BuyerReview(int Id, string Name, int PrintQuality, int FabricQuality, ReviewFit Fit, string Comment,
    DateTime CreatedAt, DateTime? UpdatedAt, string? Reply, DateTime? RepliedAt);
public sealed record ReviewPage(int Count, double Average, List<BuyerReview> Reviews);

public sealed class ProductReviewService(IDbContextFactory<AppDbContext> factory, AuthService auth)
{
    private static IQueryable<ProductReview> Published(AppDbContext db, int productId) => db.ProductReviews.AsNoTracking().Where(r =>
        r.ProductId == productId && !r.IsHidden && r.Order.UserId == r.UserId && r.Order.PaidAt != null
        && (r.Order.Status == OrderStatus.Paid || r.Order.Status == OrderStatus.Processing || r.Order.Status == OrderStatus.Shipped || r.Order.Status == OrderStatus.Delivered)
        && r.Order.Items.Any(i => i.ProductId == r.ProductId));

    public async Task<ReviewPage> ReadAsync(int productId, int page = 0)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = Published(db, productId);
        var count = await query.CountAsync();
        var average = count == 0 ? 0 : await query.AverageAsync(r => (r.PrintQuality + r.FabricQuality) / 2.0);
        var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip(Math.Clamp(page, 0, 100000) * 5).Take(5)
            .Select(r => new { r.Id, r.User.FullName, r.PrintQuality, r.FabricQuality, r.Fit, r.Comment, r.CreatedAt, r.UpdatedAt, r.AdminReply, r.AdminRepliedAt }).ToListAsync();
        return new(count, average, rows.Select(r => new BuyerReview(r.Id, PublicName(r.FullName), r.PrintQuality, r.FabricQuality, r.Fit,
            r.Comment, r.CreatedAt, r.UpdatedAt, r.AdminReply, r.AdminRepliedAt)).ToList());
    }
    public static string PublicName(string? fullName)
    {
        var first = (fullName ?? "").Replace('\u200c', ' ').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) || first.Any(char.IsDigit) || first.Contains('@') ? "خریدار پاپی" : first;
    }
    public static string FitLabel(ReviewFit fit) => fit switch { ReviewFit.Small => "کوچک‌تر از انتظار", ReviewFit.TrueToSize => "اندازه و مناسب", _ => "آزادتر از انتظار" };
    public async Task<bool> CanReviewAsync(int productId)
    {
        if (auth.CurrentUserId is not { } userId) return false;
        await using var db = await factory.CreateDbContextAsync();
        return await Eligible(db, userId, productId).AnyAsync();
    }
    private static IQueryable<Order> Eligible(AppDbContext db, int userId, int productId) => db.Orders.Where(o =>
        o.UserId == userId && o.PaidAt != null && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Processing || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered)
        && o.Items.Any(i => i.ProductId == productId));
    public async Task<ProductReview?> MineAsync(int productId)
    {
        if (auth.CurrentUserId is not { } userId) return null;
        await using var db = await factory.CreateDbContextAsync();
        return await db.ProductReviews.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.ProductId == productId);
    }
    public async Task<string?> SaveAsync(int productId, ReviewInput input)
    {
        if (auth.CurrentUserId is not { } userId) return "ابتدا وارد حساب خود شوید.";
        if (input.PrintQuality is < 1 or > 5 || input.FabricQuality is < 1 or > 5 || !Enum.IsDefined(input.Fit)
            || string.IsNullOrWhiteSpace(input.Comment) || input.Comment.Trim().Length > 2000) return "امتیازها و متن نظر را کامل کنید (حداکثر ۲۰۰۰ کاراکتر).";
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var lockId = -(((long)userId << 32) | (uint)productId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})");
        var order = await Eligible(db, userId, productId).OrderByDescending(o => o.PaidAt).FirstOrDefaultAsync();
        if (order is null || !await db.Products.AnyAsync(p => p.Id == productId && p.IsActive)) return "ثبت نظر فقط برای خریداران این محصول امکان‌پذیر است.";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Orders\" WHERE \"Id\" = {order.Id} FOR SHARE");
        if (!await Eligible(db, userId, productId).AnyAsync(o => o.Id == order.Id)) return "سفارش شما شرایط ثبت نظر را ندارد.";
        var review = await db.ProductReviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);
        if (review is null) { review = new() { ProductId = productId, UserId = userId, OrderId = order.Id }; db.ProductReviews.Add(review); }
        else { review.UpdatedAt = DateTime.UtcNow; review.OrderId = order.Id; }
        review.PrintQuality = input.PrintQuality; review.FabricQuality = input.FabricQuality; review.Fit = input.Fit; review.Comment = input.Comment.Trim();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return null;
    }
    private async Task<bool> IsAdminAsync(AppDbContext db) => auth.CurrentUserId is { } id && await db.Users.AnyAsync(u => u.Id == id && u.IsAdmin);
    public async Task<List<ProductReview>> AdminReadAsync(bool unanswered, int page)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsAdminAsync(db)) throw new UnauthorizedAccessException();
        return await db.ProductReviews.AsNoTracking().Include(r => r.Product).Include(r => r.User)
            .Where(r => !unanswered || r.AdminReply == null).OrderByDescending(r => r.CreatedAt).Skip(Math.Max(0, page) * 20).Take(21).ToListAsync();
    }
    public async Task<bool> ReplyAsync(int reviewId, string reply)
    {
        if (reply.Trim().Length > 2000) return false;
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsAdminAsync(db)) return false;
        var review = await db.ProductReviews.FindAsync(reviewId);
        if (review is null) return false;
        review.AdminReply = string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();
        review.AdminRepliedAt = review.AdminReply is null ? null : DateTime.UtcNow;
        await db.SaveChangesAsync(); return true;
    }
    public async Task<bool> SetHiddenAsync(int reviewId, bool hidden)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsAdminAsync(db)) return false;
        return await db.ProductReviews.Where(r => r.Id == reviewId).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsHidden, hidden)) == 1;
    }
}
