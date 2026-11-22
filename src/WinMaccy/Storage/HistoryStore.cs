using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMaccy.Models;

namespace WinMaccy.Storage;

/// <summary>
/// Local-only clipboard history, one JSON file (Maccy keeps a local SQLite store;
/// JSON is plenty for a few hundred items and is trivially auditable).
/// Pinned items are never pruned. Images live as PNG files under /images.
/// </summary>
public sealed class HistoryStore
{
    private sealed class HistoryFile
    {
        public List<ClipItem> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly List<ClipItem> _items = new();
    private CancellationTokenSource? _saveCts;

    public event EventHandler? Changed;

    public IReadOnlyList<ClipItem> Snapshot()
    {
        lock (_gate) return _items.ToList();
    }

    public void Load()
    {
        lock (_gate)
        {
            _items.Clear();
            try
            {
                if (File.Exists(StoragePaths.HistoryFile))
                {
                    var file = JsonSerializer.Deserialize<HistoryFile>(
                        File.ReadAllText(StoragePaths.HistoryFile), JsonOptions);
                    if (file?.Items is not null) _items.AddRange(file.Items);
                }
            }
            catch
            {
                // A broken history file is backed up, then ignored.
                try
                {
                    if (File.Exists(StoragePaths.HistoryFile))
                        File.Move(StoragePaths.HistoryFile,
                            StoragePaths.HistoryFile + ".corrupt-" + DateTimeOffset.Now.ToUnixTimeSeconds(),
                            overwrite: true);
                }
                catch { }
            }
        }
        DeleteOrphanImages();
    }

    /// <summary>Remove PNGs in /images no longer referenced by any history entry.</summary>
    private void DeleteOrphanImages()
    {
        try
        {
            if (!Directory.Exists(StoragePaths.ImagesDir)) return;
            HashSet<string> referenced;
            lock (_gate)
            {
                referenced = _items
                    .Where(i => i.Kind == ClipKind.Image && !string.IsNullOrEmpty(i.ImagePath))
                    .Select(i => Path.GetFullPath(StoragePaths.ResolveImage(i.ImagePath!)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            foreach (var file in Directory.EnumerateFiles(StoragePaths.ImagesDir, "*.png"))
            {
                if (!referenced.Contains(Path.GetFullPath(file)))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Add a new clip, or promote an existing identical one to the top.</summary>
    public void AddOrPromote(ClipItem item, int maxItems)
    {
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(i => i.Hash == item.Hash);
            if (existing is not null)
            {
                // Duplicate copy (WM_CLIPBOARDUPDATE can fire twice): drop the freshly
                // encoded image file so repeated copies don't leak PNGs in /images.
                if (item.Kind == ClipKind.Image && !string.IsNullOrEmpty(item.ImagePath)
                    && !string.Equals(item.ImagePath, existing.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteImage(item);
                }
                existing.LastUsedAt = DateTimeOffset.Now;
                existing.UseCount++;
                _items.Remove(existing);
                // Pinned items keep their section; non-pinned promoted item goes on top.
                InsertOrdered(existing);
            }
            else
            {
                _items.Insert(0, item);
            }
            Prune(maxItems);
            ScheduleSave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pinned first (oldest pin first, Maccy-style permanent section), then newest first.</summary>
    private void InsertOrdered(ClipItem item)
    {
        if (!item.Pinned) { _items.Insert(0, item); return; }
        var idx = _items.FindIndex(i => !i.Pinned);
        if (idx < 0) idx = _items.Count;
        _items.Insert(idx, item);
    }

    private void Prune(int maxItems)
    {
        var removable = _items.Where(i => !i.Pinned).ToList();
        if (removable.Count <= maxItems) return;
        var toRemove = removable
            .OrderByDescending(i => i.LastUsedAt)
            .Skip(maxItems)
            .ToHashSet();
        foreach (var item in toRemove)
        {
            TryDeleteImage(item);
            _items.Remove(item);
        }
    }

    public void TogglePin(Guid id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            _items.Remove(item);
            item.Pinned = !item.Pinned;
            if (item.Pinned) item.PinnedAt = DateTimeOffset.Now;
            InsertOrdered(item);
            ScheduleSave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            TryDeleteImage(item);
            _items.Remove(item);
            ScheduleSave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear(bool includePinned)
    {
        lock (_gate)
        {
            var doomed = _items.Where(i => includePinned || !i.Pinned).ToList();
            foreach (var item in doomed)
            {
                TryDeleteImage(item);
                _items.Remove(item);
            }
            ScheduleSave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Touch(Guid id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            item.LastUsedAt = DateTimeOffset.Now;
            item.UseCount++;
            if (!item.Pinned)
            {
                _items.Remove(item);
                _items.Insert(0, item);
            }
            ScheduleSave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void TryDeleteImage(ClipItem item)
    {
        if (item.Kind == ClipKind.Image && !string.IsNullOrEmpty(item.ImagePath))
        {
            try
            {
                var path = StoragePaths.ResolveImage(item.ImagePath);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }

    /// <summary>Debounced save: a burst of copies hits disk once.</summary>
    private void ScheduleSave()
    {
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _saveCts, cts);
        try { old?.Cancel(); old?.Dispose(); } catch { }
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(400, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            List<ClipItem> copy;
            lock (_gate) copy = _items.ToList();
            try
            {
                StoragePaths.EnsureCreated();
                var tmp = StoragePaths.HistoryFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new HistoryFile { Items = copy }, JsonOptions));
                File.Move(tmp, StoragePaths.HistoryFile, overwrite: true);
            }
            catch { }
        }, token);
    }
}
