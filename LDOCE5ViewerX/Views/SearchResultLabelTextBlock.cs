using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;
using LDOCE5ViewerX.Views.Theming;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Renders styled search-result label runs in an Avalonia <see cref="TextBlock"/>.
/// </summary>
public sealed class SearchResultLabelTextBlock : TextBlock
{
    /// <summary>
    /// Default base font size used by result-label runs.
    /// </summary>
    public const double DefaultBaseFontSize = AppConfiguration.DefaultSearchListBaseFontSize;
    private static readonly ThemedBrush HeadwordForegroundBrush = ThemedBrushes.Create("#773300", "#e0a060");
    private static readonly ThemedBrush MutedForegroundBrush = ThemedBrushes.Create("#777777", "#b0b0b0");
    private static readonly ThemedBrush AccentForegroundBrush = ThemedBrushes.Create("#208080", "#76caca");
    private static readonly ThemedBrush StrongForegroundBrush = ThemedBrushes.Create("#333333", "#e0e0e0");
    private static readonly ThemedBrush NormalForegroundBrush = ThemedBrushes.Create("#888888", "#b8b8b8");

    /// <summary>
    /// Defines the styled label runs to render.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<SearchResultLabelRun>?> LabelRunsProperty =
        AvaloniaProperty.Register<SearchResultLabelTextBlock, IReadOnlyList<SearchResultLabelRun>?>(nameof(LabelRuns));

    /// <summary>
    /// Defines the base font size used by result-label runs.
    /// </summary>
    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<SearchResultLabelTextBlock, double>(nameof(BaseFontSize), DefaultBaseFontSize);

    /// <summary>
    /// Gets or sets the styled label runs to render.
    /// </summary>
    public IReadOnlyList<SearchResultLabelRun>? LabelRuns
    {
        get => GetValue(LabelRunsProperty);
        set => SetValue(LabelRunsProperty, value);
    }

    /// <summary>
    /// Gets or sets the base font size used by result-label runs.
    /// </summary>
    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>
    /// Creates a search result label text block.
    /// </summary>
    public SearchResultLabelTextBlock()
    {
        FontSize = DefaultBaseFontSize;
        Foreground = NormalForegroundBrush.Select(this);
        ActualThemeVariantChanged += (_, _) =>
        {
            Foreground = NormalForegroundBrush.Select(this);
            UpdateInlines();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelRunsProperty || change.Property == BaseFontSizeProperty)
        {
            FontSize = BaseFontSize;
            Foreground = NormalForegroundBrush.Select(this);
            UpdateInlines();
        }
    }

    private void UpdateInlines()
    {
        Inlines?.Clear();
        if (LabelRuns is not { Count: > 0 })
        {
            return;
        }

        InlineCollection inlines = Inlines ??= [];
        foreach (SearchResultLabelRun labelRun in LabelRuns)
        {
            inlines.Add(CreateRun(labelRun));
        }
    }

    private Run CreateRun(SearchResultLabelRun labelRun)
    {
        Run run = new(labelRun.Text);
        switch (labelRun.Style)
        {
            case SearchResultLabelStyle.Headword:
                run.Foreground = HeadwordForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.HeadwordStrong:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = HeadwordForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.PhrasalVerb:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = HeadwordForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.PartOfSpeech:
                run.FontSize = Math.Floor(BaseFontSize * 0.9);
                run.FontStyle = FontStyle.Italic;
                run.Foreground = MutedForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.Superscript:
                run.BaselineAlignment = BaselineAlignment.Superscript;
                run.FontSize = Math.Floor(BaseFontSize * 0.75);
                break;
            case SearchResultLabelStyle.ActivatorConcept:
                run.FontSize = Math.Floor(BaseFontSize * 0.9);
                run.FontWeight = FontWeight.Bold;
                break;
            case SearchResultLabelStyle.ActivatorExponent:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = AccentForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.CollocationText:
                run.Foreground = StrongForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.PhraseText:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = HeadwordForegroundBrush.Select(this);
                break;
            case SearchResultLabelStyle.Normal:
            default:
                run.Foreground = NormalForegroundBrush.Select(this);
                break;
        }

        return run;
    }
}
