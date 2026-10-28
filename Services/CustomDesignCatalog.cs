namespace Papey.Services;

public static class CustomDesignCatalog
{
    public sealed record Colour(string Id, string Name, string Hex);
    public sealed record Garment(string Id, string Name, Colour[] Colours);
    public static readonly Colour White = new("white", "سفید", "#f4f2eb"), Black = new("black", "مشکی", "#20201f");
    public static readonly Garment[] Garments = [
        new("tshirt", "تیشرت", [White, Black]),
        new("hoodie", "هودی", [White, Black, new("grey", "طوسی", "#777878"), new("brown", "قهوه‌ای", "#75523d"), new("red", "زرشکی", "#781e2c")]),
        new("long-sleeve", "لانگ اسلیو", [White, Black])
    ];
    public static readonly string[] Sizes = ["M", "L", "XL", "2XL"];
    public static bool Valid(string garment, string colour, string size) =>
        Garments.Any(g => g.Id == garment && g.Colours.Any(c => c.Id == colour)) && Sizes.Contains(size);
    public static string Image(string garment, string colour, string side) => $"/img/custom-studio/{garment}-{colour}-{side}.webp";
    public static string GarmentName(string garment) => Garments.FirstOrDefault(g => g.Id == garment)?.Name ?? garment;
    public static string ColourName(string garment, string colour) => Garments.FirstOrDefault(g => g.Id == garment)?.Colours.FirstOrDefault(c => c.Id == colour)?.Name ?? colour;
    public static string StatusName(Models.CustomDesignStatus status) => status switch {
        Models.CustomDesignStatus.New => "جدید", Models.CustomDesignStatus.Contacted => "تماس گرفته شد",
        Models.CustomDesignStatus.Agreed => "توافق شد", Models.CustomDesignStatus.Completed => "تکمیل شد", _ => "لغو شد"
    };
}
