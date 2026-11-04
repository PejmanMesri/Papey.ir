using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Papey.Models;

public sealed class CustomCallbackRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(11)] public string Phone { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CustomDesignStatus Status { get; set; }
    [MaxLength(2000)] public string AdminNotes { get; set; } = "";
    public List<CustomDesignSmsJob> SmsJobs { get; set; } = [];
    [NotMapped] public string Number => "CB-" + Id.ToString("N")[..12].ToUpperInvariant();
}
