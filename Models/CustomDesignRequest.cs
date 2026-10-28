using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

public enum CustomDesignStatus { New, Contacted, Agreed, Completed, Cancelled }

public sealed class CustomDesignRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionKey { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(11)] public string Phone { get; set; } = "";
    [MaxLength(30)] public string Garment { get; set; } = "";
    [MaxLength(20)] public string Colour { get; set; } = "";
    [MaxLength(10)] public string Size { get; set; } = "";
    public int Quantity { get; set; } = 1;
    [MaxLength(2000)] public string Notes { get; set; } = "";
    public string LayersJson { get; set; } = "[]";
    public CustomDesignStatus Status { get; set; }
    [MaxLength(2000)] public string AdminNotes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CustomDesignSmsJob> SmsJobs { get; set; } = [];
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Number => "CD-" + Id.ToString("N")[..12].ToUpperInvariant();
}

public sealed class CustomDesignSmsJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public CustomDesignRequest Request { get; set; } = null!;
    [MaxLength(11)] public string Phone { get; set; } = "";
    public ShippingSmsState State { get; set; }
    public bool IsSandbox { get; set; }
    public int Attempts { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public long? MessageId { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
}

public sealed record DesignLayer(int File, string Side, double X, double Y, double Width, double Height, double Rotation);
