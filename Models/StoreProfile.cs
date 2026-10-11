namespace Papey.Models;

/// <summary>Store details rendered on printable invoices. Configure these values in appsettings.json.</summary>
public class StoreProfile
{
    public string Name { get; set; } = "پاپی";
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public string EconomicCode { get; set; } = string.Empty;
}