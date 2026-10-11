using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

/// <summary>Records a product a signed-in customer wants to keep for later.</summary>
public class ProductFavorite
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}