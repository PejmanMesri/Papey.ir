using System.ComponentModel.DataAnnotations;
namespace Papey.Models;

public sealed class StorePageContent
{
    [MaxLength(20)] public string Id { get; set; } = "";
    public string ContentJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
