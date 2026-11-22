using System.IO;
using System.Windows;
using WinMaccy.Interop;
using WinMaccy.Services;
using WinMaccy.Storage;
using WinMaccy.UI;

namespace WinMaccy;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\WinMaccy-Singleton-ClipboardHistory";

    private Mutex? _singleInstance;
    private SettingsStore _settings = null!;
    private HistoryStore _history = null!;
    private ClipboardCaptureService _capture = null!;
    private PasteService _paste = null!;
    private NativeMessageWindow _native = null!;
    private TrayIconService _tray = null!;
    private PopupWindow _popup = null!;
    private SettingsWindow? _settingsWindow;
    private bool _shuttingDown;

    // Diagnostic logging is opt-in: set WINMACCY_DEBUG=1, then debug.log is written
    // to the data directory (portable folder or %APPDATA%\WinMaccy).
    internal static bool DebugEnabled { get; } =
        Environment.GetEnvironmentVariable("WINMACCY_DEBUG") is "1" or "true";

    internal static string LogPath => StoragePaths.LogFile;

    internal static void Log(string message)
    {
        if (!DebugEnabled) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ev) => Log("DISPATCHER UNHANDLED: " + ev.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Log("APPDOMAIN UNHANDLED: " + ev.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
            Log("UNOBSERVED TASK: " + ev.Exception);

        Log("OnStartup; args=" + string.Join(',', e.Args));
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out bool createdNew);
        if (!createdNew)
        {
            // Another WinMaccy is already running for this user session.
            Shutdown();
            return;
        }

        StoragePaths.EnsureCreated();

        _settings = new SettingsStore();
        _settings.Load();
        _settings.Changed += (_, _) => ApplyTheme();

        _history = new HistoryStore();
        _history.Load();

        _capture = new ClipboardCaptureService(_history, _settings);
        _paste = new PasteService(_capture);

        _native = new NativeMessageWindow();
        _native.Create();
        _native.StartClipboardListener();
        _native.ClipboardUpdated += (_, _) =>
        {
            if (!_shuttingDown)
            {
                try { _capture.OnClipboardUpdated(); }
                catch (Exception ex) { Log("capture threw: " + ex); }
            }
        };
        _native.HotKeyPressed += (_, _) => OpenPopup();

        _native.TryRegisterHotKey(_settings.Current.HotKey);

        _popup = new PopupWindow(_history, _paste, _settings.Current);

        _tray = new TrayIconService(
            settings: _settings,
            history: _history,
            onOpen: OpenPopup,
            onRecent: item =>
            {
                _history.Touch(item.Id);
                _paste.Apply(item, paste: false, plainTextOnly: false);
            },
            onIgnoreNext: () =>
            {
                _capture.SuppressNext = true;
                _tray.Notify("WinMaccy", "将忽略下一次复制。");
            },
            onClearUnpinned: () =>
            {
                _history.Clear(includePinned: false);
                _tray.Notify("WinMaccy", "已清空非置顶历史。");
            },
            onSettings: OpenSettings,
            onExit: ShutdownApp);

        ApplyTheme();

        bool minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        bool wantSettings = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase);

        if (wantSettings)
        {
            // --settings: launch straight into the settings window.
            Dispatcher.BeginInvoke(new Action(OpenSettings),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (!minimized)
        {
            // First interactive launch: greet from the tray; autostart passes --minimized.
            Dispatcher.BeginInvoke(new Action(() =>
                _tray.Notify("WinMaccy",
                    "WinMaccy 已在后台运行。按 " + _settings.Current.HotKey.Display + " 唤起剪贴板历史。")),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OpenPopup()
    {
        if (_shuttingDown) return;
        if (_popup.IsVisible) _popup.Hide();
        _popup.Open();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _native, _history);
        _settingsWindow.ShowDialog();
    }

    private void ApplyTheme()
    {
        var mode = _settings.Current.Theme;
        bool dark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => ThemeService.IsSystemDark()
        };
        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = source };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dict);
    }

    private void ShutdownApp()
    {
        _shuttingDown = true;
        try { _native.Dispose(); } catch { }
        try { _tray.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); } catch { }
        Shutdown();
    }
}
