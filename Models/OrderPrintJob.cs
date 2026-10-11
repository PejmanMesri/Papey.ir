using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

/// <summary>One durable receipt job per successfully paid order.</summary>
public class OrderPrintJob
{
    public Guid Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime AvailableAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public int? ClaimedByUserId { get; set; }
    [MaxLength(100)] public string? ClientId { get; set; }
    public int Attempts { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
}
