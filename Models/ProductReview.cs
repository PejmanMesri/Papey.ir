using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

public enum ReviewFit { Small = 1, TrueToSize = 2, Loose = 3 }

public class ProductReview
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [Range(1, 5)] public int PrintQuality { get; set; }
    [Range(1, 5)] public int FabricQuality { get; set; }
    public ReviewFit Fit { get; set; }
    [Required, MaxLength(2000)] public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsHidden { get; set; }
    [MaxLength(2000)] public string? AdminReply { get; set; }
    public DateTime? AdminRepliedAt { get; set; }
}
