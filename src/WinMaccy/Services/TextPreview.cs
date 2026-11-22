using System.Text;
using System.Text.RegularExpressions;

namespace WinMaccy.Services;

/// <summary>Builds the single-line list preview, Maccy-style.</summary>
public static partial class TextPreview
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Create(string? text, int max = 160)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var collapsed = Whitespace().Replace(text.Trim(), " ");
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}
