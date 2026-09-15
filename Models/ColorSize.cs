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

/// <summary>Product type: t-shirt now; pants/shirts can be added later.</summary>
public enum ProductType
{
    TShirt,
    Shirt,
    Pants
}
