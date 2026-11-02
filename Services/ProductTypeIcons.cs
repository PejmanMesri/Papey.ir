namespace Papey.Services;

public static class ProductTypeIcons
{
    public static string? Image(string? name, string? value)
    {
        var normalized = (name ?? "").Trim().ToLowerInvariant().Replace('ي', 'ی').Replace("‌", "").Replace(" ", "").Replace("-", "");
        var icon = value?.Trim().ToLowerInvariant();
        // Explicit choices take priority over automatic name matching.
        var selected = icon switch
        {
            "tshirt" or "t-shirt" or "👕" => "tshirt",
            "hoodie" or "🧥" => "hoodie",
            "long-sleeve" => "long-sleeve",
            "clothing" => "clothing",
            _ => null
        };
        if (selected is not null) return $"/img/icons/{selected}.svg";
        if (!string.IsNullOrWhiteSpace(value) && value != "—") return null;
        if (normalized.Contains("لانگاسلیو") || normalized.Contains("longsleeve") || icon == "long-sleeve") return "/img/icons/long-sleeve.svg";
        if (normalized.Contains("هودی") || normalized == "hoodie" || icon is "hoodie" or "🧥") return "/img/icons/hoodie.svg";
        if (normalized.Contains("تیشرت") || normalized is "tshirt" or "tee" || icon is "tshirt" or "t-shirt" or "👕") return "/img/icons/tshirt.svg";
        return string.IsNullOrWhiteSpace(value) || value == "—" ? "/img/icons/clothing.svg" : null;
    }
}
