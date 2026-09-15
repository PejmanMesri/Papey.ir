using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

public enum MediaType
{
    Image,
    Video
}

/// <summary>A photo or video belonging to a product, shown in a gallery like digikala.</summary>
public class ProductMedia
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public MediaType Type { get; set; } = MediaType.Image;

    [MaxLength(200)]
    public string? Alt { get; set; }

    public int DisplayOrder { get; set; }
}
