using Microsoft.EntityFrameworkCore;
using Papey.Models;

namespace Papey.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<ProductDesignMedia> ProductDesignMedia => Set<ProductDesignMedia>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Color> Colors => Set<Color>();
    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<ProductTypeEntity> ProductTypes => Set<ProductTypeEntity>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderAdminSmsJob> OrderAdminSmsJobs => Set<OrderAdminSmsJob>();
    public DbSet<ShippingSmsJob> ShippingSmsJobs => Set<ShippingSmsJob>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderPrintJob> OrderPrintJobs => Set<OrderPrintJob>();
    public DbSet<ProductFavorite> ProductFavorites => Set<ProductFavorite>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<PageView> PageViews => Set<PageView>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Category>().HasIndex(c => c.Slug).IsUnique();
        mb.Entity<Category>()
            .HasOne(c => c.ProductType)
            .WithMany(t => t.Categories)
            .HasForeignKey(c => c.ProductTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Category>().HasIndex(c => new { c.ProductTypeId, c.DisplayOrder });
        mb.Entity<Product>().Property(p => p.HasUnlimitedStock).HasDefaultValue(true);
        mb.Entity<ProductVariant>().Property(v => v.IsEnabled).HasDefaultValue(true);
        mb.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
        mb.Entity<Product>().HasIndex(p => p.HomeHeroSlot).IsUnique()
            .HasFilter("\"HomeHeroSlot\" IS NOT NULL");
        mb.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Products_HomeHeroSlot", "\"HomeHeroSlot\" IS NULL OR \"HomeHeroSlot\" BETWEEN 1 AND 2"));
        mb.Entity<ProductCategory>().HasKey(pc => new { pc.ProductId, pc.CategoryId });
        mb.Entity<ProductCategory>()
            .HasOne(pc => pc.Product).WithMany(p => p.ProductCategories)
            .HasForeignKey(pc => pc.ProductId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProductCategory>()
            .HasOne(pc => pc.Category).WithMany()
            .HasForeignKey(pc => pc.CategoryId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<OrderAdminSmsJob>().HasIndex(j => new { j.OrderId, j.Phone }).IsUnique();
        mb.Entity<OrderAdminSmsJob>().HasIndex(j => new { j.State, j.AvailableAt });
        mb.Entity<OrderAdminSmsJob>().HasOne(j => j.Order).WithMany().HasForeignKey(j => j.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ShippingSmsJob>().HasIndex(j => j.OrderId).IsUnique();
        mb.Entity<ShippingSmsJob>().HasIndex(j => new { j.State, j.AvailableAt });
        mb.Entity<ShippingSmsJob>().HasOne(j => j.Order).WithMany().HasForeignKey(j => j.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        mb.Entity<OrderPrintJob>().Property(j => j.Id).HasDefaultValueSql("gen_random_uuid()");
        mb.Entity<OrderPrintJob>().Property(j => j.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        mb.Entity<OrderPrintJob>().Property(j => j.AvailableAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        mb.Entity<OrderPrintJob>().HasIndex(j => j.OrderId).IsUnique();
        mb.Entity<OrderPrintJob>().HasIndex(j => new { j.AvailableAt, j.CreatedAt })
            .HasFilter("\"PrintedAt\" IS NULL");
        mb.Entity<OrderPrintJob>().HasOne(j => j.Order).WithMany()
            .HasForeignKey(j => j.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProductFavorite>().HasIndex(f => new { f.UserId, f.ProductId }).IsUnique();
        mb.Entity<ProductFavorite>()
            .HasOne(f => f.User).WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProductFavorite>()
            .HasOne(f => f.Product).WithMany(p => p.FavoritedBy)
            .HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OtpCode>().HasIndex(o => o.Phone);
    }
}
