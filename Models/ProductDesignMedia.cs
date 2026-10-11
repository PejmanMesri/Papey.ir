using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

/// <summary>
/// An admin-only source image for a product print design. Files are stored outside wwwroot.
/// </summary>
public class ProductDesignMedia
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(120)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required, MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ContentType { get; set; } = "image/jpeg";

    public long FileSize { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
