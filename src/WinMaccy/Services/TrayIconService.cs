using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinMaccy.Models;
using WinMaccy.Storage;

namespace WinMaccy.Services;

/// <summary>
/// Tray icon + context menu (the Windows equivalent of Maccy's menu-bar item).
/// Like Maccy, the recent history items are listed directly at the top of the
/// menu and clicking one copies it. WinForms NotifyIcon is used on purpose: it
/// is the only zero-dependency tray implementation shipping with the SDK.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int RecentCount = 6;

    private readonly SettingsStore _settings;
    private readonly HistoryStore _history;
    private readonly Action<ClipItem> _onRecent;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly List<ToolStripItem> _dynamicItems = new();

    public TrayIconService(
        SettingsStore settings,
        HistoryStore history,
        Action onOpen,
        Action<ClipItem> onRecent,
        Action onIgnoreNext,
        Action onClearUnpinned,
        Action onSettings,
        Action onExit)
    {
        _settings = settings;
        _history = history;
        _onRecent = onRecent;
        _menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开剪贴板历史\tCtrl+Shift+C", null, (_, _) => onOpen());
        _pauseItem = new ToolStripMenuItem("暂停记录", null, (_, _) => TogglePaused()) { CheckOnClick = false };
        var ignoreNextItem = new ToolStripMenuItem("忽略下一次复制", null, (_, _) => onIgnoreNext());
        var clearItem = new ToolStripMenuItem("清空历史（保留置顶）", null, (_, _) => onClearUnpinned());
        var settingsItem = new ToolStripMenuItem("设置…", null, (_, _) => onSettings());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => onExit());

        _menu.Items.AddRange(new ToolStripItem[]
        {
            // Dynamic recent items + separator are inserted at index 0 on Opening.
            openItem,
            new ToolStripSeparator(),
            _pauseItem,
            ignoreNextItem,
            new ToolStripSeparator(),
            clearItem,
            settingsItem,
            new ToolStripSeparator(),
            exitItem
        });
        _menu.Opening += (_, _) =>
        {
            bool paused = _settings.Current.Paused;
            _pauseItem.Text = paused ? "继续记录" : "暂停记录";
            _pauseItem.Checked = paused;
            RebuildRecentItems();
        };

        _icon = new NotifyIcon
        {
            Text = "WinMaccy 剪贴板历史",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = _menu
        };
        // Single left click and double click both open the popup like Maccy's
        // menu bar item. (Right click is handled automatically: it opens _menu.)
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) onOpen();
        };
        _icon.DoubleClick += (_, _) => onOpen();
    }

    private void RebuildRecentItems()
    {
        foreach (var old in _dynamicItems)
        {
            _menu.Items.Remove(old);
            if (old is ToolStripMenuItem mi) mi.Image?.Dispose();
            old.Dispose();
        }
        _dynamicItems.Clear();

        int insert = 0;
        foreach (var item in _history.Snapshot().Take(RecentCount))
        {
            var mi = new ToolStripMenuItem(FormatLabel(item), MenuImage(item), (_, _) => _onRecent(item));
            _menu.Items.Insert(insert++, mi);
            _dynamicItems.Add(mi);
        }
        if (_dynamicItems.Count > 0)
        {
            var sep = new ToolStripSeparator();
            _menu.Items.Insert(insert, sep);
            _dynamicItems.Add(sep);
        }
    }

    private static string FormatLabel(ClipItem item)
    {
        var text = (item.Preview ?? string.Empty).Replace("&", "&&");
        string prefix = item.Kind switch
        {
            ClipKind.Image => "🖼 ",
            ClipKind.Files => "📄 ",
            ClipKind.RichText => "¶ ",
            _ => string.Empty
        };
        text = prefix + text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 64 ? text[..64] + "…" : text;
    }

    private static Image? MenuImage(ClipItem item)
    {
        try
        {
            if (item.Kind == ClipKind.Image && !string.IsNullOrEmpty(item.ImagePath))
            {
                var path = StoragePaths.ResolveImage(item.ImagePath);
                if (File.Exists(path))
                {
                    using var src = Image.FromFile(path);
                    return new Bitmap(src, 16, 16);
                }
            }
            if (!string.IsNullOrEmpty(item.SourcePath) && File.Exists(item.SourcePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(item.SourcePath);
                return icon?.ToBitmap();
            }
        }
        catch { }
        return null;
    }

    public void Notify(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(3000);
        }
        catch { }
    }

    private void TogglePaused()
    {
        _settings.Update(s => s.Paused = !s.Paused);
    }

    private static Icon LoadIcon()
    {
        // Prefer the icon embedded in the assembly (also correct when launched
        // via `dotnet exec`, where ProcessPath points at dotnet.exe).
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Icon(stream);
            }
        }
        catch { }
        // Fall back to the icon bound to our own executable.
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return extracted;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        try
        {
            foreach (var d in _dynamicItems)
            {
                if (d is ToolStripMenuItem mi) mi.Image?.Dispose();
                d.Dispose();
            }
            _icon.Visible = false;
            _menu.Dispose();
            _icon.Dispose();
        }
        catch { }
    }
}
