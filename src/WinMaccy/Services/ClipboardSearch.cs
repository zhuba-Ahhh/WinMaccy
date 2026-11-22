using WinMaccy.Models;

namespace WinMaccy.Services;

/// <summary>
/// Fast in-memory search over history. Maccy does instant substring filtering;
/// here every whitespace-separated token must appear (case-insensitive), which
/// gives a light "fuzzy" feel without any index at a few hundred items.
/// </summary>
public static class ClipboardSearch
{
    public static IEnumerable<ClipItem> Apply(IEnumerable<ClipItem> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Where(i => Matches(i, tokens));
    }

    private static bool Matches(ClipItem item, string[] tokens)
    {
        var haystack = item.Preview;
        if (item.Kind == ClipKind.Files && item.Paths is not null)
            haystack += " " + string.Join(' ', item.Paths);
        if (item.Kind == ClipKind.Image) haystack += " image 图片";
        if (item.Kind == ClipKind.RichText) haystack += " richtext 富文本";
        haystack = haystack.ToLowerInvariant();
        return tokens.All(t => haystack.Contains(t.ToLowerInvariant(), StringComparison.Ordinal));
    }
}
