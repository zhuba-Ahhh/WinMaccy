using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace WinMaccy.Models;

/// <summary>
/// One clipboard history entry. Mirrors Maccy's HistoryItem: multiple payload
/// formats (text / html / rtf / image / file paths) hang off one record.
/// </summary>
public sealed class ClipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipKind Kind { get; set; }

    /// <summary>Single-line, whitespace-collapsed preview shown in the list.</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>CF_UNICODETEXT payload (also kept for image items that carried text).</summary>
    public string? Text { get; set; }

    /// <summary>Raw CF_HTML payload, including the version header.</summary>
    public string? Html { get; set; }

    /// <summary>Raw RTF payload.</summary>
    public string? Rtf { get; set; }

    /// <summary>Absolute path of the stored PNG file for image items.</summary>
    public string? ImagePath { get; set; }

    /// <summary>Copied file / folder paths for Files items.</summary>
    public List<string>? Paths { get; set; }

    /// <summary>Content hash used for de-duplication / re-promotion.</summary>
    public string Hash { get; set; } = string.Empty;

    public string? SourceApp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? PinnedAt { get; set; }
    public int UseCount { get; set; }

    [JsonIgnore]
    public bool Pinned
    {
        get => PinnedAt is not null;
        set => PinnedAt = value ? DateTimeOffset.Now : null;
    }

    /// <summary>Decoded list thumbnail, populated on demand by the UI (never persisted).</summary>
    [JsonIgnore]
    public BitmapSource? Thumbnail { get; set; }
}
