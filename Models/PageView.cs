using System.ComponentModel.DataAnnotations;

namespace Papey.Models;

/// <summary>One row per page view — used for daily traffic stats in the admin dashboard.</summary>
public class PageView
{
    public long Id { get; set; }

    [MaxLength(300)]
    public string Path { get; set; } = string.Empty;

    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
