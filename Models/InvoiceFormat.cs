namespace Papey.Models;

public enum InvoiceFormat
{
    Standard,
    Official,
    Thermal
}

public static class InvoiceFormatExtensions
{
    public static InvoiceFormat Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "official" => InvoiceFormat.Official,
        "thermal" => InvoiceFormat.Thermal,
        _ => InvoiceFormat.Standard
    };

    public static string RouteSegment(this InvoiceFormat format) => format switch
    {
        InvoiceFormat.Official => "official",
        InvoiceFormat.Thermal => "thermal",
        _ => "standard"
    };

    public static string Label(this InvoiceFormat format) => format switch
    {
        InvoiceFormat.Official => "فاکتور رسمی",
        InvoiceFormat.Thermal => "پرینت حرارتی",
        _ => "فاکتور"
    };
}