using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinMaccy.Models;
using WinMaccy.Services;
using WinMaccy.Storage;
using static WinMaccy.Interop.NativeMethods;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WinMaccy.UI;

/// <summary>
/// The Maccy-style popup: search box on top, ranked result list, shortcut footer.
/// Opened with the global hotkey, dismissed on Esc / deactivation.
/// </summary>
public partial class PopupWindow : Window
{
    private readonly HistoryStore _history;
    private readonly PasteService _paste;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<ClipItem> _items = new();
    private List<ClipItem> _all = new();

    /// <summary>Foreground window captured before the popup steals focus (paste target).</summary>
    private IntPtr _restoreTarget;

    public PopupWindow(HistoryStore history, PasteService paste, AppSettings settings)
    {
        _history = history;
        _paste = paste;
        _settings = settings;
        InitializeComponent();

        ResultsList.ItemsSource = _items;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Open the popup, reload history, center over the previously active window.</summary>
    public void Open()
    {
        // Must be sampled while the user's app is still foreground.
        _restoreTarget = GetForegroundWindow();

        Reload();
        SearchBox.Text = string.Empty;
        FooterBorder.Visibility = _settings.ShowFooter ? Visibility.Visible : Visibility.Collapsed;

        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SelectFirst();
    }

    private void Reload()
    {
        _all = _history.Snapshot().ToList();
        ApplyQuery(SearchBox.Text);
    }

    /// <summary>Filter + fuzzy-rank into the bound collection.</summary>
    private void ApplyQuery(string? query)
    {
        _items.Clear();
        foreach (var it in ClipboardSearch.Search(_all, query)) _items.Add(it);

        bool querying = !string.IsNullOrWhiteSpace(query);
        bool hasAny = _items.Count > 0;
        EmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        ResultsList.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = querying
            ? "未找到匹配项"
            : "暂无历史记录，复制任意内容后即会出现";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        CenterOnCurrentMonitor(helper.Handle);
    }

    private void CenterOnCurrentMonitor(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi)) return;

        uint dpi = GetDpiForWindow(hwnd); // 96 == 100%
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var work = mi.rcWork;
        double workW = (work.Right - work.Left) / scale;
        double workH = (work.Bottom - work.Top) / scale;
        double w = Width;
        double h = Math.Min(Math.Max(ActualHeight > 0 ? ActualHeight : 420, MinHeight), MaxHeight);

        Left = work.Left / scale + (workW - w) / 2.0;
        Top = work.Top / scale + Math.Max(8, (workH - h) / 2.0 - workH * 0.06);
    }

    // ---- search ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyQuery(SearchBox.Text);
        SelectFirst();
    }

    private void SelectFirst()
    {
        if (_items.Count == 0) { ResultsList.UnselectAll(); return; }
        ResultsList.SelectedIndex = 0;
    }

    // ---- keyboard ----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        // With Alt held WPF reports Key.System; the real key lives in SystemKey (Alt+P).
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                return;
            case Key.Down when !alt:
                MoveSelection(1);
                e.Handled = true;
                return;
            case Key.Up when !alt:
                MoveSelection(-1);
                e.Handled = true;
                return;
            case Key.Enter:
                // Enter: honor AutoPaste; Shift forces copy-only; Ctrl forces plain text.
                ActivateSelected(paste: !shift && _settings.AutoPaste, plainTextOnly: ctrl);
                e.Handled = true;
                return;
            case Key.Delete when ctrl && shift:
                // Maccy's Option+Cmd+Delete: clear everything except pinned.
                _history.Clear(false);
                Reload();
                e.Handled = true;
                return;
            case Key.Delete when !ctrl && !shift && !alt:
                DeleteSelected();
                e.Handled = true;
                return;
            case Key.P when alt:
                TogglePinSelected();
                e.Handled = true;
                return;
        }

        // Ctrl+1..9 quick select (Windows port of Maccy's Cmd+1..9).
        if (ctrl && !alt && key >= Key.D1 && key <= Key.D9)
        {
            int idx = key - Key.D1;
            if (idx < _items.Count && _items[idx] is { } item)
                Activate(item, paste: !shift && _settings.AutoPaste, plainTextOnly: false);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        int idx = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex + delta;
        idx = Math.Clamp(idx, 0, _items.Count - 1);
        ResultsList.SelectedIndex = idx;
        ResultsList.ScrollIntoView(_items[idx]);
    }

    private void ActivateSelected(bool paste, bool plainTextOnly)
    {
        if (ResultsList.SelectedItem is ClipItem item) Activate(item, paste, plainTextOnly);
    }

    private void Activate(ClipItem item, bool paste, bool plainTextOnly)
    {
        _history.Touch(item.Id);
        _paste.Apply(item, paste, plainTextOnly, _restoreTarget);
        Hide();
    }

    private void DeleteSelected()
    {
        if (ResultsList.SelectedItem is not ClipItem item) return;
        int idx = ResultsList.SelectedIndex;
        _history.Delete(item.Id);
        Reload();
        if (_items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Min(Math.Max(0, idx), _items.Count - 1);
        }
        SearchBox.Focus();
    }

    private void TogglePinSelected()
    {
        if (ResultsList.SelectedItem is not ClipItem item) return;
        _history.TogglePin(item.Id);
        var query = SearchBox.Text;
        Reload();
        SearchBox.Focus();
    }

    // ---- row hover actions ----

    private void RowPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            _history.TogglePin(item.Id);
            Reload();
            SearchBox.Focus();
        }
        e.Handled = true;
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            int idx = _items.IndexOf(item);
            _history.Delete(item.Id);
            Reload();
            if (_items.Count > 0)
                ResultsList.SelectedIndex = Math.Min(Math.Max(0, idx), _items.Count - 1);
            SearchBox.Focus();
        }
        e.Handled = true;
    }

    // ---- mouse ----

    // Maccy mouse semantics: click copies, Alt+click pastes, Alt+Shift+click pastes plain text.
    private void ResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(src) is not null) return;
        if (FindAncestor<ListBoxItem>(src)?.DataContext is not ClipItem item) return;

        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Activate(item, paste: alt, plainTextOnly: alt && shift);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T match) return match;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d)
                ?? (d as FrameworkElement)?.Parent;
        }
        return null;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        // Close-on-focus-loss like a spotlight popup, but stay alive for next hotkey.
        Hide();
        base.OnDeactivated(e);
    }
}
