using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

public class Color
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Hex { get; set; } = "#000000";

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Size
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Product type lookup (تیشرت, هودی, شلوار, ...) — editable in the admin panel.</summary>
public class ProductTypeEntity
{
    [MaxLength(500)] public string? SizeGuideImageUrl { get; set; }
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? FilterIcon { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Category> Categories { get; set; } = new();
}
