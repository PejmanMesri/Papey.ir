using System.ComponentModel.DataAnnotations;
namespace Papey.Models;



public sealed class OrderAdminSmsJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [MaxLength(11)] public string Phone { get; set; } = "";
    public bool IsSandbox { get; set; }
    public ShippingSmsState State { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public long? MessageId { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
}
