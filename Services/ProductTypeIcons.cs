namespace Papey.Services;

public static class ProductTypeIcons
{
    public static string? Image(string? name, string? value)
    {
        var normalized = (name ?? "").Trim().ToLowerInvariant().Replace('ي', 'ی').Replace("‌", "").Replace(" ", "").Replace("-", "");
        var icon = value?.Trim().ToLowerInvariant();
        if (normalized.Contains("لانگاسلیو") || normalized.Contains("longsleeve") || icon == "long-sleeve") return "/img/icons/long-sleeve.svg";
        if (normalized.Contains("هودی") || normalized == "hoodie" || icon is "hoodie" or "🧥") return "/img/icons/hoodie.svg";
        if (normalized.Contains("تیشرت") || normalized is "tshirt" or "tee" || icon is "tshirt" or "t-shirt" or "👕") return "/img/icons/tshirt.svg";
        return string.IsNullOrWhiteSpace(value) || value == "—" ? "/img/icons/clothing.svg" : null;
    }
}
