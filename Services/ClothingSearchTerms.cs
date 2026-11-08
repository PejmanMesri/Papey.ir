namespace Papey.Services;
public static class ClothingSearchTerms {
    public static string PrintedName(string name) {
        name=name.Trim();
        if(new[]{"بیسیک","ساده","بدون چاپ","basic","plain"}.Any(term=>name.Contains(term,StringComparison.OrdinalIgnoreCase))) return name;
        if(name.Contains("چاپ")||name.Contains("کاستوم")) return name;
        return name+" چاپی";
    }
}
