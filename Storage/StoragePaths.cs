namespace WinMaccy.Storage;

public static class StoragePaths
{
    // Resolution order:
    //   1. WINMACCY_DATA_DIR environment variable (explicit override)
    //   2. "data" folder next to the executable (portable/green build)
    //   3. %APPDATA%\WinMaccy (standard per-user install)
    public static string RootDir { get; } = ResolveRoot();

    public static bool IsPortable { get; private set; }

    public static string HistoryFile => Path.Combine(RootDir, "history.json");
    public static string SettingsFile => Path.Combine(RootDir, "settings.json");
    public static string LogFile => Path.Combine(RootDir, "debug.log");
    public static string ImagesDir => Path.Combine(RootDir, "images");

    /// <summary>
    /// Resolve a stored image reference to a full path. New entries store a bare file name
    /// (so the portable folder stays relocatable); legacy absolute paths still work.
    /// </summary>
    public static string ResolveImage(string stored)
        => Path.IsPathRooted(stored) ? stored : Path.Combine(ImagesDir, stored);

    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("WINMACCY_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            IsPortable = true;
            return Path.GetFullPath(env);
        }

        var baseDir = AppContext.BaseDirectory;
        var portableDir = Path.Combine(baseDir, "data");
        if (Directory.Exists(portableDir))
        {
            IsPortable = true;
            return portableDir;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMaccy");
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ImagesDir);
    }
}
