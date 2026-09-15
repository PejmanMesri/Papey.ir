using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

/// <summary>One-time password for phone-number login.</summary>
public class OtpCode
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public int Attempts { get; set; }

    public bool IsValid => !IsUsed && ExpiresAt > DateTime.UtcNow && Attempts < 5;
}
