using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMaccy.Models;
using WinMaccy.Services;
using WinMaccy.Storage;

namespace WinMaccy.Converters;

/// <summary>ClipKind → Segoe Fluent Icons glyph shown on the left of each row.</summary>
public sealed class KindGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ClipKind.RichText => "\uE8A5", // document with text
            ClipKind.Image => "\uEB9F",    // photo
            ClipKind.Files => "\uE8B7",    // folder
            _ => "\uE8C8"                  // copy / text
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Secondary line: size/dimensions/count plus relative time.</summary>
public sealed class MetaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ClipItem item) return string.Empty;
        var main = item.Kind switch
        {
            ClipKind.Image when !string.IsNullOrEmpty(item.ImagePath) => "图片",
            ClipKind.Files when item.Paths is { Count: > 0 } => item.Paths.Count + " 个项目",
            ClipKind.RichText => "富文本",
            _ => (item.Text?.Length ?? 0) + " 字符"
        };
        var parts = new List<string> { main };
        if (!string.IsNullOrWhiteSpace(item.SourceApp)) parts.Add(item.SourceApp!);
        parts.Add(TimeAgo(item.LastUsedAt));
        return string.Join("  ·  ", parts);
    }

    private static string TimeAgo(DateTimeOffset t)
    {
        var span = DateTimeOffset.Now - t;
        if (span.TotalSeconds < 60) return "刚刚";
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
        if (span.TotalHours < 24) return (int)span.TotalHours + " 小时前";
        if (span.TotalDays < 30) return (int)span.TotalDays + " 天前";
        return t.ToString("yyyy-MM-dd");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Image item → small frozen thumbnail (cached); other kinds → null.</summary>
public sealed class ThumbnailConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ClipItem { Kind: ClipKind.Image, ImagePath: { } stored })
            return null;
        var path = StoragePaths.ResolveImage(stored);
        if (!File.Exists(path)) return null;
        if (Cache.TryGetValue(stored, out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96; // 2x of 48px slot, crisp on HiDPI
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            Cache[stored] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>ClipItem → source application icon (cached); image items / unknown source → null.</summary>
public sealed class AppIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ClipItem { Kind: not ClipKind.Image, SourcePath: { } path }) return null;
        return AppInfoService.GetIcon(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility, inverted (true hides). Used to swap badge ↔ hover actions.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Badge converters take the ListBoxItem container and resolve its live position in the
/// (possibly filtered) item generator, so the shown number always matches Ctrl+1..9.
/// </summary>
internal static class BadgeIndex
{
    public static int FromContainer(object? value)
    {
        if (value is not DependencyObject container) return -1;
        var owner = ItemsControl.ItemsControlFromItemContainer(container);
        if (owner is null) return -1;
        return owner.ItemContainerGenerator.IndexFromContainer(container);
    }
}

/// <summary>Container → Visibility for the Ctrl+1..9 quick-select badge.</summary>
public sealed class BadgeVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BadgeIndex.FromContainer(value) is >= 0 and <= 8 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>[container, isMouseOver] → badge Visibility: top 9 rows unless hover actions are shown.</summary>
public sealed class BadgeShowConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 1 && values[1] is true) return Visibility.Collapsed;
        return values.Length > 0 && BadgeIndex.FromContainer(values[0]) is >= 0 and <= 8
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Container → badge text "1".."9".</summary>
public sealed class BadgeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var i = BadgeIndex.FromContainer(value);
        return i is >= 0 and <= 8 ? (i + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
