namespace Papey.Models;

/// <summary>Session-scoped cart item (not persisted to DB).</summary>
public class CartItem
{
    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Color { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#000";
    public string Size { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int MaxStock { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}
