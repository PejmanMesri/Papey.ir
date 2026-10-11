using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Papey.Models;

public class Product
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(220)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal Price { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal? OldPrice { get; set; }

    [MaxLength(300)]
    public string? ImageUrl { get; set; }

    public bool IsFeatured { get; set; }
    public bool IsNew { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Product type (lookup table, editable in admin) — تیشرت، هودی، شلوار...</summary>
    public int? ProductTypeId { get; set; }
    public ProductTypeEntity? ProductType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>Additional categories this product belongs to (many-to-many).</summary>
    public List<ProductCategory> ProductCategories { get; set; } = new();

    public List<ProductVariant> Variants { get; set; } = new();
    public List<ProductMedia> Media { get; set; } = new();
    public List<ProductDesignMedia> DesignMedia { get; set; } = new();
    public List<ProductFavorite> FavoritedBy { get; set; } = new();
}

/// <summary>Join table: a cloth can belong to multiple categories.</summary>
public class ProductCategory
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class ProductVariant
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Color { get; set; } = string.Empty;

    [MaxLength(20)]
    public string ColorHex { get; set; } = "#000000";

    public int? ColorId { get; set; }
    public Color? ColorRef { get; set; }

    [Required, MaxLength(10)]
    public string Size { get; set; } = string.Empty;

    public int? SizeId { get; set; }
    public Size? SizeRef { get; set; }

    public int Stock { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal? PriceOverride { get; set; }

    public decimal EffectivePrice => PriceOverride ?? Product.Price;
}
