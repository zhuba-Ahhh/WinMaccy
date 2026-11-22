using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using WinMaccy.Interop;
using WinMaccy.Models;
using WinMaccy.Storage;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;

namespace WinMaccy.Services;

/// <summary>
/// Captures the clipboard when WM_CLIPBOARDUPDATE fires. Clipboard COM marshalling
/// (Clipboard.GetDataObject) must run on the dispatcher/STA thread; heavy image
/// encode + disk write happen on a background task.
/// </summary>
public sealed class ClipboardCaptureService
{
    private static readonly int[] RetryDelaysMs = [20, 40, 80, 140, 220];

    private readonly HistoryStore _history;
    private readonly SettingsStore _settings;
    private readonly object _gate = new();
    private bool _running;
    private bool _pending;

    /// <summary>Raised on the dispatcher thread whenever history changes.</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>Our own copy into the clipboard should not re-enter history.</summary>
    public bool SuppressNext { get; set; }

    public ClipboardCaptureService(HistoryStore history, SettingsStore settings)
    {
        _history = history;
        _settings = settings;
    }

    public void OnClipboardUpdated()
    {
        App.Log("capture: OnClipboardUpdated entered; paused=" + _settings.Current.Paused
            + " suppress=" + SuppressNext + " thread=" + Environment.CurrentManagedThreadId);
        if (_settings.Current.Paused) return;
        lock (_gate)
        {
            if (_running) { _pending = true; return; }
            _running = true;
        }
        _ = CaptureLoopAsync();
    }

    private async Task CaptureLoopAsync()
    {
        try
        {
            while (true)
            {
                await CaptureOnceAsync().ConfigureAwait(true);
                lock (_gate)
                {
                    if (!_pending) { _running = false; return; }
                    _pending = false;
                }
            }
        }
        catch
        {
            lock (_gate) { _running = false; _pending = false; }
        }
    }

    private async Task CaptureOnceAsync()
    {
        if (_settings.Current.Paused) return;

        System.Windows.IDataObject? data = null;
        for (var i = 0; i < RetryDelaysMs.Length; i++)
        {
            try
            {
                data = Clipboard.GetDataObject();
                if (data is not null) break;
            }
            catch { /* source still owns the clipboard; back off */ }
            if (i < RetryDelaysMs.Length - 1)
                await Task.Delay(RetryDelaysMs[i]).ConfigureAwait(true);
        }
        if (data is null) { App.Log("capture: IDataObject null after retries"); return; }

        if (SuppressNext)
        {
            SuppressNext = false;
            App.Log("capture: suppressed (our own write)");
            return;
        }

        var settings = _settings.Current;
        var source = AppInfoService.GetForegroundApp();
        if (settings.IgnoredApps.Count > 0 && IsIgnored(settings.IgnoredApps, source))
        {
            return;
        }

        ClipItem? item;
        try
        {
            item = await Task.Run(() => BuildItem(data, settings, source.Name, source.ExePath)).ConfigureAwait(true);
        }
        catch { item = null; }
        if (item is null) { App.Log("capture: BuildItem returned null (unsupported/empty)"); return; }

        App.Log("capture: adding item kind=" + item.Kind + " preview=" + item.Preview);
        _history.AddOrPromote(item, settings.MaxItems);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsIgnored(List<string> ignored, AppInfoService.AppInfo source)
    {
        // Match either the friendly product name or exe file name, e.g. "chrome" / "Google Chrome".
        string? procName = null;
        try
        {
            if (!string.IsNullOrEmpty(source.ExePath))
                procName = Path.GetFileNameWithoutExtension(source.ExePath);
        }
        catch { }
        return Contains(ignored, source.Name) || Contains(ignored, procName);

        static bool Contains(List<string> list, string? name)
            => !string.IsNullOrEmpty(name) && list.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    // Runs on a background thread. The IDataObject handed across was already
    // marshalled by WPF's Clipboard class and exposes plain string/BitmapSource data.
    private ClipItem? BuildItem(System.Windows.IDataObject data, AppSettings settings,
        string? sourceName, string? sourcePath)
    {
        // 1) Files
        if (settings.CaptureFiles && data.GetDataPresent(DataFormats.FileDrop)
            && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            return new ClipItem
            {
                Kind = ClipKind.Files,
                Paths = paths.ToList(),
                Preview = BuildFilesPreview(paths),
                Hash = ContentHasher.ComputePaths(paths),
                SourceApp = sourceName,
                SourcePath = sourcePath
            };
        }

        // 2) Image (screenshot tools offer Bitmap; Explorer image files handled above only as files)
        if (settings.CaptureImages && data.GetDataPresent(DataFormats.Bitmap)
            && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
        {
            if (bmp.PixelWidth * bmp.PixelHeight > 64_000_000) return null;
            string? imageRef = SavePng(bmp, settings); // bare file name under /images
            if (imageRef is null) return null;
            var text = TryGetText(data);
            return new ClipItem
            {
                Kind = ClipKind.Image,
                ImagePath = imageRef,
                Text = text,
                Preview = string.IsNullOrWhiteSpace(text)
                    ? $"图片  {bmp.PixelWidth}×{bmp.PixelHeight}"
                    : TextPreview.Create(text),
                Hash = "IMG:" + HashFile(StoragePaths.ResolveImage(imageRef)),
                SourceApp = sourceName,
                SourcePath = sourcePath
            };
        }

        // 3) Text / HTML / RTF
        var plain = TryGetText(data);
        string? html = data.GetDataPresent(DataFormats.Html) ? data.GetData(DataFormats.Html) as string : null;
        string? rtf = data.GetDataPresent(DataFormats.Rtf) ? data.GetData(DataFormats.Rtf) as string : null;
        if (string.IsNullOrEmpty(plain) && string.IsNullOrEmpty(html) && string.IsNullOrEmpty(rtf)) return null;

        var kind = (!string.IsNullOrEmpty(html) || !string.IsNullOrEmpty(rtf)) ? ClipKind.RichText : ClipKind.Text;
        return new ClipItem
        {
            Kind = kind,
            Text = plain,
            Html = html,
            Rtf = rtf,
            Preview = TextPreview.Create(plain),
            Hash = ContentHasher.Compute(kind.ToString(), plain, rtf),
            SourceApp = sourceName,
            SourcePath = sourcePath
        };
    }

    private static string? TryGetText(System.Windows.IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.UnicodeText)) return data.GetData(DataFormats.UnicodeText) as string;
            if (data.GetDataPresent(DataFormats.Text)) return data.GetData(DataFormats.Text) as string;
        }
        catch { }
        return null;
    }

    private static string BuildFilesPreview(string[] paths)
    {
        var names = paths.Take(3).Select(Path.GetFileName);
        var text = string.Join(", ", names);
        if (paths.Length > 3) text += $"  (+{paths.Length - 3})";
        return (paths.Length == 1 && Directory.Exists(paths[0]) ? "文件夹  " : "文件  ") + text;
    }

    /// <summary>Encodes the bitmap to /images and returns the bare stored file name.</summary>
    private static string? SavePng(BitmapSource source, AppSettings settings)
    {
        try
        {
            StoragePaths.EnsureCreated();
            var fileName = Guid.NewGuid().ToString("N") + ".png";
            var path = Path.Combine(StoragePaths.ImagesDir, fileName);
            var frame = source as BitmapFrame ?? BitmapFrame.Create(source);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(frame);
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            encoder.Save(fs);
            if (new FileInfo(path).Length > (long)settings.MaxImageMb * 1024 * 1024)
            {
                File.Delete(path);
                return null;
            }
            return fileName;
        }
        catch { return null; }
    }

    private static string HashFile(string path)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch { return Guid.NewGuid().ToString("N"); }
    }
}
