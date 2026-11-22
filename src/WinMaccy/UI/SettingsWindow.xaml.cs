using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinMaccy.Interop;
using WinMaccy.Models;
using WinMaccy.Services;
using WinMaccy.Storage;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace WinMaccy.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly NativeMessageWindow _native;
    private readonly HistoryStore _history;

    private bool _recording;
    private HotKeyConfig _draftHotKey;

    public SettingsWindow(SettingsStore settings, NativeMessageWindow native, HistoryStore history)
    {
        _settings = settings;
        _native = native;
        _history = history;
        _draftHotKey = settings.Current.HotKey.Clone();
        InitializeComponent();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        ChkAutoPaste.IsChecked = s.AutoPaste;
        ChkStartWithWindows.IsChecked = AutoStartService.IsEnabled();
        ChkCaptureImages.IsChecked = s.CaptureImages;
        ChkCaptureFiles.IsChecked = s.CaptureFiles;
        MaxItemsBox.Text = s.MaxItems.ToString();
        MaxImageBox.Text = s.MaxImageMb.ToString();
        IgnoredAppsBox.Text = string.Join(", ", s.IgnoredApps);
        ThemeCombo.SelectedIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        HotKeyButton.Content = s.HotKey.Display;
    }

    // ---- hotkey recorder ----

    private void HotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _recording = true;
        HotKeyButton.Content = "请按下快捷键…（Esc 取消）";
        Keyboard.Focus(HotKeyButton);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _recording = false;
            HotKeyButton.Content = _draftHotKey.Display;
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool win = e.Key == Key.LWin || e.Key == Key.RWin
                   || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

        // Modifier-only presses: keep listening.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        var candidate = new HotKeyConfig
        {
            Ctrl = ctrl, Alt = alt, Shift = shift, Win = win, VirtualKey = vk
        };
        if (!candidate.IsValid) return;

        _draftHotKey = candidate;
        _recording = false;
        HotKeyButton.Content = candidate.Display;
    }

    // ---- actions ----

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        int maxItems = ParseInt(MaxItemsBox.Text, 200, 10, 10000);
        int maxImage = ParseInt(MaxImageBox.Text, 10, 1, 100);
        var ignored = IgnoredAppsBox.Text
            .Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var theme = ThemeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        var autoStart = ChkStartWithWindows.IsChecked == true;
        var hotKey = _draftHotKey;

        _settings.Update(s =>
        {
            s.AutoPaste = ChkAutoPaste.IsChecked == true;
            s.StartWithWindows = autoStart;
            s.CaptureImages = ChkCaptureImages.IsChecked == true;
            s.CaptureFiles = ChkCaptureFiles.IsChecked == true;
            s.MaxItems = maxItems;
            s.MaxImageMb = maxImage;
            s.IgnoredApps = ignored;
            s.Theme = theme;
            s.HotKey = hotKey;
        });

        AutoStartService.Set(autoStart);

        if (!_native.TryRegisterHotKey(hotKey))
            MessageBox.Show(this,
                "快捷键 " + hotKey.Display + " 注册失败，可能已被其他程序占用。\n可稍后在设置中更换。",
                "WinMaccy", MessageBoxButton.OK, MessageBoxImage.Warning);

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("确定清空全部非置顶历史记录？")) { _history.Clear(false); }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("确定清空全部历史记录（包含置顶项）？此操作不可撤销。")) { _history.Clear(true); }
    }

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "WinMaccy", MessageBoxButton.YesNo, MessageBoxImage.Warning)
        == MessageBoxResult.Yes;

    private static int ParseInt(string text, int fallback, int min, int max)
        => int.TryParse(text.Trim(), out var v) ? Math.Clamp(v, min, max) : fallback;
}
