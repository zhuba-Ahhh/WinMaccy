using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMaccy.Interop;

namespace WinMaccy.Services;

/// <summary>
/// Resolves the foreground application's name / exe path and caches its icon,
/// so history rows can show the real source app icon like Maccy does on macOS.
/// </summary>
public static class AppInfoService
{
    public readonly record struct AppInfo(string? Name, string? ExePath);

    private static readonly Dictionary<string, ImageSource?> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Foreground app at this moment: friendly product name + exe path.</summary>
    public static AppInfo GetForegroundApp()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return default;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return default;
            using var p = Process.GetProcessById((int)pid);
            string? path = TryGetExePath(p);
            string name = FriendlyName(p, path);
            return new AppInfo(name, path);
        }
        catch
        {
            return default;
        }
    }

    private static string? TryGetExePath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { /* protected/UWP process */ return null; }
    }

    private static string FriendlyName(Process p, string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                {
                    var desc = vi.FileDescription.Trim();
                    // Some packaged apps report e.g. "Notepad.exe" as the description.
                    if (desc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        desc = Path.GetFileNameWithoutExtension(desc);
                    if (desc.Length > 0) return desc;
                }
            }
        }
        catch { }
        return p.ProcessName;
    }

    /// <summary>32px icon of an executable (cached); null if it cannot be extracted.</summary>
    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        lock (IconCache)
        {
            if (IconCache.TryGetValue(exePath, out var cached)) return cached;
            ImageSource? source = null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null)
                {
                    source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                }
            }
            catch { source = null; }
            IconCache[exePath] = source;
            return source;
        }
    }
}
