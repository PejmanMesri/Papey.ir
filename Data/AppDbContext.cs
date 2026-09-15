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
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Color> Colors => Set<Color>();
    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<ProductTypeEntity> ProductTypes => Set<ProductTypeEntity>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<PageView> PageViews => Set<PageView>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Category>().HasIndex(c => c.Slug).IsUnique();
        mb.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
        mb.Entity<ProductCategory>().HasKey(pc => new { pc.ProductId, pc.CategoryId });
        mb.Entity<ProductCategory>()
            .HasOne(pc => pc.Product).WithMany(p => p.ProductCategories)
            .HasForeignKey(pc => pc.ProductId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProductCategory>()
            .HasOne(pc => pc.Category).WithMany()
            .HasForeignKey(pc => pc.CategoryId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        mb.Entity<OtpCode>().HasIndex(o => o.Phone);
    }
}
