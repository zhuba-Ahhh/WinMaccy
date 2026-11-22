using System.Security.Cryptography;
using System.Text;

namespace WinMaccy.Services;

public static class ContentHasher
{
    public static string Compute(params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts) sb.Append(p ?? string.Empty).Append('\u001F');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string ComputePaths(IEnumerable<string> paths)
        => Compute("FILES:" + string.Join("|", paths.Select(Path.GetFullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)));
}
