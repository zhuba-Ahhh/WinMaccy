namespace WinMaccy.Models;

public sealed class AppSettings
{
    /// <summary>Maccy's "Paste automatically": selecting an item also pastes it.</summary>
    public bool AutoPaste { get; set; } = true;

    /// <summary>Maximum number of unpinned history items (Maccy default 200).</summary>
    public int MaxItems { get; set; } = 200;

    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;

    /// <summary>Temporarily ignore every new copy (tray "暂停记录" / Maccy ignoreEvents).</summary>
    public bool Paused { get; set; }

    /// <summary>"System" | "Dark" | "Light".</summary>
    public string Theme { get; set; } = "System";

    public HotKeyConfig HotKey { get; set; } = new();

    /// <summary>Process names (without .exe) whose copies are ignored.</summary>
    public List<string> IgnoredApps { get; set; } = new();

    public bool StartWithWindows { get; set; }

    /// <summary>Reject images larger than this many megabytes.</summary>
    public int MaxImageMb { get; set; } = 10;
}
