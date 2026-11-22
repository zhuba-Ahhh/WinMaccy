using System.Drawing;
using System.Windows.Forms;
using WinMaccy.Storage;

namespace WinMaccy.Services;

/// <summary>
/// Tray icon + context menu (the Windows equivalent of Maccy's menu-bar item).
/// WinForms NotifyIcon is used on purpose: it is the only zero-dependency tray
/// implementation shipping with the .NET SDK.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;

    public TrayIconService(
        SettingsStore settings,
        Action onOpen,
        Action onIgnoreNext,
        Action onClearUnpinned,
        Action onSettings,
        Action onExit)
    {
        _settings = settings;

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开剪贴板历史\tCtrl+Shift+C", null, (_, _) => onOpen());
        _pauseItem = new ToolStripMenuItem("暂停记录", null, (_, _) => TogglePaused()) { CheckOnClick = false };
        var ignoreNextItem = new ToolStripMenuItem("忽略下一次复制", null, (_, _) => onIgnoreNext());
        var clearItem = new ToolStripMenuItem("清空历史（保留置顶）", null, (_, _) => onClearUnpinned());
        var settingsItem = new ToolStripMenuItem("设置…", null, (_, _) => onSettings());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => onExit());

        menu.Items.AddRange(new ToolStripItem[]
        {
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
        menu.Opening += (_, _) =>
        {
            bool paused = _settings.Current.Paused;
            _pauseItem.Text = paused ? "继续记录" : "暂停记录";
            _pauseItem.Checked = paused;
        };

        _icon = new NotifyIcon
        {
            Text = "WinMaccy 剪贴板历史",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        // Double-click / single-click both open the popup like Maccy's menu bar.
        _icon.DoubleClick += (_, _) => onOpen();
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
        // Prefer the icon bound to our own executable.
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
        try { _icon.Visible = false; _icon.Dispose(); } catch { }
    }
}
