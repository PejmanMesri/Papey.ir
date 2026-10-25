using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Papey.Models;

public enum OrderStatus
{
    Pending,
    Paid,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    PaymentFailed
}

public class Order
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string OrderNumber { get; set; } = string.Empty;

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Province { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string FullAddress { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal ShippingCost { get; set; }

    public bool TaxEnabled { get; set; }

    [Column(TypeName = "numeric(5,2)")]
    public decimal TaxRatePercent { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal GrandTotal { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    [MaxLength(100)]
    public string? PaymentAuthority { get; set; }

    [MaxLength(50)]
    public string? PaymentRefId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    [MaxLength(25)] public string? CourierName { get; set; }
    [MaxLength(25)] public string? TrackingNumber { get; set; }
    public DateTime? ShippedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Color { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Size { get; set; } = string.Empty;

    [Column(TypeName = "numeric(18,0)")]
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal LineTotal { get; set; }
}
