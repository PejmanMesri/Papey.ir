namespace Papey.Services;

public static class ProductSizeGuides
{
    public const string ShirtImage = "/img/size-guide-default.jpg";
    public const string HoodieImage = "/img/size-guide-hoodie-default.jpg";

    public static string Resolve(string? customImage, string? productTypeName)
    {
        if (!string.IsNullOrWhiteSpace(customImage)) return customImage;
        var type = productTypeName?.Trim().Replace('ي', 'ی');
        return type == "هودی" || string.Equals(type, "hoodie", StringComparison.OrdinalIgnoreCase)
            ? HoodieImage : ShirtImage;
    }
}
