using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Services;

/// <summary>Manages the current customer's persisted product favorites.</summary>
public class FavoriteService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AuthService _auth;

    public FavoriteService(IDbContextFactory<AppDbContext> dbFactory, AuthService auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;
    }

    public event Action? OnChange;

    public bool IsSignedIn => _auth.CurrentUserId is not null;

    public async Task<HashSet<int>> GetProductIdsAsync()
    {
        if (_auth.CurrentUserId is not { } userId) return [];

        await using var db = await _dbFactory.CreateDbContextAsync();
        return (await db.ProductFavorites
            .Where(f => f.UserId == userId)
            .Select(f => f.ProductId)
            .ToListAsync())
            .ToHashSet();
    }

    public async Task<int> GetCountAsync()
    {
        if (_auth.CurrentUserId is not { } userId) return 0;

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ProductFavorites.CountAsync(f => f.UserId == userId);
    }

    /// <summary>Returns false when the visitor must sign in before changing favorites.</summary>
    public async Task<bool> ToggleAsync(int productId)
    {
        if (_auth.CurrentUserId is not { } userId) return false;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.ProductFavorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

        if (existing is null)
            db.ProductFavorites.Add(new() { UserId = userId, ProductId = productId });
        else
            db.ProductFavorites.Remove(existing);

        await db.SaveChangesAsync();
        OnChange?.Invoke();
        return true;
    }
}