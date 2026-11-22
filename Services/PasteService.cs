using WinMaccy.Interop;

namespace WinMaccy.Services;

/// <summary>
/// Puts a selected item on the clipboard and, when AutoPaste is on, synthesizes
/// Ctrl+V into the previously focused window (Maccy's "Paste automatically",
/// which requires Accessibility permission on macOS but nothing on Windows).
/// </summary>
public sealed class PasteService
{
    public ClipboardCaptureService Capture { get; }

    public PasteService(ClipboardCaptureService capture) => Capture = capture;

    /// <param name="paste">true = select AND paste; false = copy to clipboard only.</param>
    /// <param name="plainTextOnly">Maccy's Option+Shift+Enter: drop formatting.</param>
    /// <param name="target">Window to paste into; capture it before the popup takes foreground.</param>
    public void Apply(Models.ClipItem item, bool paste, bool plainTextOnly, IntPtr target = default)
    {
        if (target == IntPtr.Zero) target = NativeMethods.GetForegroundWindow();

        Capture.SuppressNext = true;
        SetClipboard(item, plainTextOnly);

        if (!paste) return;

        // Give the clipboard owner chain a beat, then send Ctrl+V to the old focus.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ForceForeground(target);
            SendControlV();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void SetClipboard(Models.ClipItem item, bool plainTextOnly)
    {
        var data = new System.Windows.DataObject();
        switch (item.Kind)
        {
            case Models.ClipKind.Files when item.Paths is { Count: > 0 }:
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(item.Paths.ToArray());
                System.Windows.Forms.Clipboard.SetFileDropList(sc);
                return;

            case Models.ClipKind.Image when !plainTextOnly && !string.IsNullOrEmpty(item.ImagePath):
                var imgFull = WinMaccy.Storage.StoragePaths.ResolveImage(item.ImagePath);
                if (!File.Exists(imgFull)) return;
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(imgFull);
                bmp.EndInit();
                bmp.Freeze();
                System.Windows.Clipboard.SetImage(bmp);
                return;

            default:
                var text = item.Text ?? string.Empty;
                if (plainTextOnly || item.Kind == Models.ClipKind.Text || string.IsNullOrEmpty(item.Rtf))
                {
                    System.Windows.Clipboard.SetText(text);
                }
                else
                {
                    data.SetText(text);
                    if (!string.IsNullOrEmpty(item.Html))
                        try { data.SetText(item.Html, System.Windows.TextDataFormat.Html); } catch { }
                    if (!string.IsNullOrEmpty(item.Rtf))
                        try { data.SetText(item.Rtf, System.Windows.TextDataFormat.Rtf); } catch { }
                    System.Windows.Clipboard.SetDataObject(data, copy: true);
                }
                return;
        }
    }

    /// <summary>AttachThreadInput trick: background WPF windows cannot normally SetForegroundWindow.</summary>
    private static void ForceForeground(IntPtr target)
    {
        if (target == IntPtr.Zero) return;
        try
        {
            if (NativeMethods.IsIconic(target)) NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);
            var foreground = NativeMethods.GetForegroundWindow();
            var targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();
            var foreThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            if (foreThread != currentThread) NativeMethods.AttachThreadInput(foreThread, currentThread, true);
            if (targetThread != currentThread) NativeMethods.AttachThreadInput(targetThread, currentThread, true);
            NativeMethods.SetForegroundWindow(target);
            if (foreThread != currentThread) NativeMethods.AttachThreadInput(foreThread, currentThread, false);
            if (targetThread != currentThread) NativeMethods.AttachThreadInput(targetThread, currentThread, false);
        }
        catch { }
    }

    private static void SendControlV()
    {
        var inputs = new NativeMethods.INPUT[4];
        inputs[0] = KeyInput(NativeMethods.VK_CONTROL, false);
        inputs[1] = KeyInput(NativeMethods.VK_V, false);
        inputs[2] = KeyInput(NativeMethods.VK_V, true);
        inputs[3] = KeyInput(NativeMethods.VK_CONTROL, true);
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}
