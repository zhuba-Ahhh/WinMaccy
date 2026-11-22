using WinMaccy.Models;

namespace WinMaccy.Services;

/// <summary>
/// Fzf-style subsequence search with stable relevance ranking. Each
/// whitespace-separated token must match (case-insensitive); contiguous runs,
/// word boundaries and exact substrings score bonus points. Also returns the
/// matched character ranges used to highlight the query inside a row.
/// </summary>
public static class ClipboardSearch
{
    public static IReadOnlyList<ClipItem> Search(IEnumerable<ClipItem> items, string? query)
    {
        List<ClipItem> all = items as List<ClipItem> ?? items.ToList();
        if (string.IsNullOrWhiteSpace(query)) return all;

        var tokens = Tokens(query);
        var hits = new List<(ClipItem Item, int Score, int Index)>();
        for (int i = 0; i < all.Count; i++)
        {
            int score = ScoreItem(all[i], tokens);
            if (score > int.MinValue) hits.Add((all[i], score, i));
        }

        // OrderBy is stable: equal scores keep history order.
        return hits.OrderByDescending(h => h.Score)
                   .ThenBy(h => h.Index)
                   .Select(h => h.Item)
                   .ToList();
    }

    private static string[] Tokens(string query)
        => query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int ScoreItem(ClipItem item, string[] tokens)
    {
        var haystack = Haystack(item);
        int total = 0;
        foreach (var token in tokens)
        {
            if (!FuzzyMatch(haystack, token, out int score, out _)) return int.MinValue;
            total += score;
        }
        // Bonus for matches inside the visible preview over deep payload fields.
        if (tokens.All(t => item.Preview.Contains(t, StringComparison.OrdinalIgnoreCase)))
            total += 30;
        return total;
    }

    private static string Haystack(ClipItem item)
    {
        var h = item.Preview;
        if (item.Paths is { Count: > 0 }) h += " " + string.Join(' ', item.Paths);
        if (!string.IsNullOrEmpty(item.SourceApp)) h += " " + item.SourceApp;
        if (item.Kind == ClipKind.Image) h += " image 图片";
        if (item.Kind == ClipKind.RichText) h += " richtext 富文本";
        return h;
    }

    /// <summary>
    /// Greedy subsequence match. Returns false when any token char is missing.
    /// </summary>
    public static bool FuzzyMatch(string text, string token, out int score, out List<(int Start, int Length)> ranges)
    {
        score = 0;
        ranges = new List<(int, int)>();
        if (token.Length == 0) return true;
        if (text.Length == 0) return false;

        // Exact (case-insensitive) contiguous occurrence: strongest signal.
        int sub = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (sub >= 0)
        {
            ranges.Add((sub, token.Length));
            score = 100 + ContiguousBonus(text, sub, token.Length);
            return true;
        }

        int ti = 0;
        int runStart = -1, runLen = 0;
        int prev = -1;
        for (int i = 0; i < text.Length && ti < token.Length; i++)
        {
            if (Char.ToLowerInvariant(text[i]) != Char.ToLowerInvariant(token[ti])) continue;

            if (runLen == 0) runStart = i;
            bool contiguous = prev == i - 1;
            if (runLen > 0 && !contiguous)
            {
                ranges.Add((runStart, runLen));
                runStart = i;
                runLen = 0;
            }
            runLen++;
            score += contiguous ? 12 : 5;
            if (IsWordStart(text, i)) score += 6;
            if (prev >= 0) score -= Math.Min(4, i - prev - 1); // gap penalty
            prev = i;
            ti++;
        }

        if (ti < token.Length) return false;
        if (runLen > 0) ranges.Add((runStart, runLen));
        return true;
    }

    private static int ContiguousBonus(string text, int start, int length)
    {
        int bonus = 0;
        for (int k = 0; k < length; k++)
            if (IsWordStart(text, start + k)) bonus += 8;
        if (start == 0) bonus += 4;
        return bonus;
    }

    private static bool IsWordStart(string text, int i)
    {
        if (i == 0) return true;
        char c = text[i], p = text[i - 1];
        return !char.IsLetterOrDigit(p)
            || (char.IsLower(p) && char.IsUpper(c));
    }

    /// <summary>Merged highlight ranges for all tokens inside the shown text.</summary>
    public static IReadOnlyList<(int Start, int Length)> HighlightRanges(string text, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || text.Length == 0)
            return Array.Empty<(int, int)>();

        var merged = new List<(int Start, int End)>();
        foreach (var token in Tokens(query))
        {
            if (!FuzzyMatch(text, token, out _, out var ranges)) continue;
            foreach (var r in ranges)
            {
                int end = r.Start + r.Length;
                int insertAt = 0;
                while (insertAt < merged.Count && merged[insertAt].End < r.Start) insertAt++;
                if (insertAt < merged.Count && merged[insertAt].Start <= end)
                {
                    var m = merged[insertAt];
                    merged[insertAt] = (Math.Min(m.Start, r.Start), Math.Max(m.End, end));
                }
                else
                {
                    merged.Insert(insertAt, (r.Start, end));
                }
            }
        }

        // Collapse any ranges the insertion merge left overlapping.
        for (int i = merged.Count - 1; i > 0; i--)
        {
            if (merged[i - 1].End >= merged[i].Start)
            {
                merged[i - 1] = (merged[i - 1].Start, Math.Max(merged[i - 1].End, merged[i].End));
                merged.RemoveAt(i);
            }
        }
        return merged.Select(m => (m.Start, m.End - m.Start)).ToList();
    }
}
