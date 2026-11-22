using System.Windows.Interop;
using WinMaccy.Models;

namespace WinMaccy.Interop;

/// <summary>
/// Message-only-ish hidden window receiving WM_CLIPBOARDUPDATE and WM_HOTKEY.
/// Created on the WPF dispatcher thread so its HwndSource has a message pump.
/// </summary>
public sealed class NativeMessageWindow : IDisposable
{
    public const int HotKeyId = 0xB001;

    private HwndSource? _source;
    private bool _listening;
    private bool _hotKeyRegistered;
    private bool _disposed;

    public event EventHandler? ClipboardUpdated;
    public event EventHandler? HotKeyPressed;

    public void Create()
    {
        if (_source is not null) return;
        var p = new HwndSourceParameters("WinMaccyMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = unchecked((int)0x80000000) // WS_POPUP
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool StartClipboardListener()
    {
        if (_source is null || _listening) return _listening;
        _listening = NativeMethods.AddClipboardFormatListener(_source.Handle);
        return _listening;
    }

    public bool TryRegisterHotKey(HotKeyConfig config)
    {
        if (_source is null || !config.IsValid) return false;
        if (_hotKeyRegistered) UnregisterHotKey();

        uint mods = NativeMethods.MOD_NOREPEAT;
        if (config.Ctrl) mods |= NativeMethods.MOD_CONTROL;
        if (config.Alt) mods |= NativeMethods.MOD_ALT;
        if (config.Shift) mods |= NativeMethods.MOD_SHIFT;
        if (config.Win) mods |= NativeMethods.MOD_WIN;

        _hotKeyRegistered =
            NativeMethods.RegisterHotKey(_source.Handle, HotKeyId, mods, (uint)config.VirtualKey);
        return _hotKeyRegistered;
    }

    public void UnregisterHotKey()
    {
        if (_source is null || !_hotKeyRegistered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotKeyId);
        _hotKeyRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_CLIPBOARDUPDATE:
                ClipboardUpdated?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case NativeMethods.WM_HOTKEY:
                if (wParam.ToInt32() == HotKeyId)
                {
                    HotKeyPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                }
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_source is not null && _listening)
            {
                NativeMethods.RemoveClipboardFormatListener(_source.Handle);
                _listening = false;
            }
        }
        catch { }
        try { UnregisterHotKey(); } catch { }
        try
        {
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
            _source = null;
        }
        catch { }
    }
}
