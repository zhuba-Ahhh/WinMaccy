using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WinMaccy.Services;

namespace WinMaccy.UI;

/// <summary>
/// TextBlock that rebuilds its inlines so every fuzzy-match range of the
/// current search query is rendered in the accent colour and semi-bold.
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.Register(nameof(Query), typeof(string), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HighlightTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        var text = SourceText ?? string.Empty;
        if (text.Length == 0) return;

        var ranges = ClipboardSearch.HighlightRanges(text, Query);
        if (ranges.Count == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush
            ?? System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush;
        int pos = 0;
        foreach (var (start, length) in ranges)
        {
            if (start > pos) Inlines.Add(new Run(text[pos..start]));
            var hit = new Run(text.Substring(start, length))
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = accent
            };
            Inlines.Add(hit);
            pos = start + length;
        }
        if (pos < text.Length) Inlines.Add(new Run(text[pos..]));
    }
}
