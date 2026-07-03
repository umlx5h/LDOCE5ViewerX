using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;
using LDOCE5ViewerX.Views.Theming;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Native Avalonia renderer for rich dictionary documents.
/// </summary>
public partial class EntryDocumentView : UserControl
{
    /// <summary>
    /// Default base font size used by rendered dictionary content.
    /// </summary>
    public const double DefaultBaseFontSize = AppConfiguration.DefaultContentBaseFontSize;

    /// <summary>
    /// Defines the document to display.
    /// </summary>
    public static readonly StyledProperty<DictionaryDocument?> DocumentProperty =
        AvaloniaProperty.Register<EntryDocumentView, DictionaryDocument?>(nameof(Document));

    /// <summary>
    /// Defines the anchor to scroll to and highlight.
    /// </summary>
    public static readonly StyledProperty<string?> HighlightedAnchorProperty =
        AvaloniaProperty.Register<EntryDocumentView, string?>(nameof(HighlightedAnchor));

    /// <summary>
    /// Defines the current find query.
    /// </summary>
    public static readonly StyledProperty<string> FindTextProperty =
        AvaloniaProperty.Register<EntryDocumentView, string>(nameof(FindText), string.Empty);

    /// <summary>
    /// Defines the visible find status.
    /// </summary>
    public static readonly StyledProperty<string> FindStatusProperty =
        AvaloniaProperty.Register<EntryDocumentView, string>(nameof(FindStatus), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the zoom factor applied to rendered dictionary content.
    /// </summary>
    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<EntryDocumentView, double>(nameof(ZoomFactor), 1.0);

    /// <summary>
    /// Defines the base font size used by rendered dictionary content.
    /// </summary>
    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<EntryDocumentView, double>(nameof(BaseFontSize), DefaultBaseFontSize);

    /// <summary>
    /// Defines whether example sentences are rendered in italic text.
    /// </summary>
    public static readonly StyledProperty<bool> IsExampleItalicEnabledProperty =
        AvaloniaProperty.Register<EntryDocumentView, bool>(nameof(IsExampleItalicEnabled), true);

    /// <summary>
    /// Defines the font family used by example sentences.
    /// </summary>
    public static readonly StyledProperty<FontFamily> ExampleFontFamilyProperty =
        AvaloniaProperty.Register<EntryDocumentView, FontFamily>(nameof(ExampleFontFamily), FontFamily.Default);

    /// <summary>
    /// Defines the command used for dictionary navigation.
    /// </summary>
    public static readonly StyledProperty<ICommand?> NavigateCommandProperty =
        AvaloniaProperty.Register<EntryDocumentView, ICommand?>(nameof(NavigateCommand));

    /// <summary>
    /// Defines the command used for lookup links.
    /// </summary>
    public static readonly StyledProperty<ICommand?> LookupCommandProperty =
        AvaloniaProperty.Register<EntryDocumentView, ICommand?>(nameof(LookupCommand));

    /// <summary>
    /// Defines the command used for audio playback.
    /// </summary>
    public static readonly StyledProperty<ICommand?> PlayAudioCommandProperty =
        AvaloniaProperty.Register<EntryDocumentView, ICommand?>(nameof(PlayAudioCommand));

    /// <summary>
    /// Defines the web search engines shown for selected text.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<WebSearchSite>?> WebSearchSitesProperty =
        AvaloniaProperty.Register<EntryDocumentView, IReadOnlyList<WebSearchSite>?>(nameof(WebSearchSites));

    private readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<DictionaryResourceRef, Bitmap> _imageCache = [];
    private readonly List<Control> _findHits = [];
    private readonly List<ScrollViewer> _contentScrollViewers = [];
    private int _activeFindIndex = -1;
    private int _renderedFindHitIndex;
    private static readonly KeyGesture LookupSelectedTextGesture = PlatformShortcuts.PrimaryGesture(Key.E);
    private static readonly KeyGesture CopySelectedTextGesture = PlatformShortcuts.PrimaryGesture(Key.C);

    private static readonly ThemedBrush DocumentPanelBackgroundBrush = ThemedBrushes.Create(Brushes.White, "#252525");
    private static readonly ThemedBrush DocumentPanelAltBackgroundBrush = ThemedBrushes.Create(Brushes.GhostWhite, "#2d2d2d");
    private static readonly ThemedBrush DocumentBorderBrushValue = ThemedBrushes.Create(Brushes.LightGray, "#555555");
    private static readonly ThemedBrush DocumentForegroundBrush = ThemedBrushes.Create(Brushes.Black, "#e8e8e8");
    private static readonly ThemedBrush DocumentMutedForegroundBrush = ThemedBrushes.Create(Brushes.DimGray, "#b0b0b0");
    private static readonly ThemedBrush DocumentHeadwordForegroundBrush = ThemedBrushes.Create(Brushes.SaddleBrown, "#e0a060");
    private static readonly ThemedBrush DocumentLinkForegroundBrush = ThemedBrushes.Create("#208080", "#76caca");
    private static readonly ThemedBrush DocumentGreenForegroundBrush = ThemedBrushes.Create("#087b44", "#74c892");
    private static readonly ThemedBrush DocumentBadgeForegroundBrush = ThemedBrushes.Create(Brushes.DarkGreen, "#83d48d");
    private static readonly ThemedBrush ActivatorSectionBorderBrushValue = ThemedBrushes.Create(Brushes.DarkCyan, "#3a9696");
    private static readonly ThemedBrush ActivatorSectionBackgroundBrush = ThemedBrushes.Create(Brushes.Azure, "#1f3838");
    private static readonly ThemedBrush ImageFrameBackgroundBrush = ThemedBrushes.Create(Brushes.White, "#f2f2f2");
    private static readonly ThemedBrush HighlightBackgroundBrush = ThemedBrushes.Create(Brushes.LemonChiffon, "#5f4d18");
    private static readonly ThemedBrush ActiveFindBackgroundBrush = ThemedBrushes.Create(Brushes.Orange, "#b96e00");
    private static readonly ThemedBrush FindBackgroundBrush = ThemedBrushes.Create(Brushes.Gold, "#776600");
    private static readonly ThemedBrush HighlightForegroundBrush = ThemedBrushes.Create(Brushes.Black, "#fff2bf");

    private static readonly IBrush SignpostTagBackground = ThemedBrushes.CreateBrush("#3f7373");
    private static readonly IBrush RelationTagBackground = ThemedBrushes.CreateBrush("#773300");
    private static readonly IBrush FrequencyTagBackground = ThemedBrushes.CreateBrush("#009a50");
    private static readonly IBrush DocumentTagForeground = Brushes.White;
    private static readonly ThemedBrush BritishAudioIconBrush = ThemedBrushes.Create("#c62828", "#eb6969");
    private static readonly ThemedBrush AmericanAudioIconBrush = ThemedBrushes.Create("#1565c0", "#4698f6");
    private static readonly ThemedBrush DefaultAudioIconBrush = ThemedBrushes.Create("#666666", "#dadada");
    private static readonly Color BoxHeadingGradientStartColor = ThemedBrushes.CreateColor("#707070");
    private static readonly Color BoxHeadingGradientEndColor = ThemedBrushes.CreateColor("#404040");

    /// <summary>
    /// Creates the document view.
    /// </summary>
    public EntryDocumentView()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => RenderDocument(resetScrollWhenNoAnchor: false, preserveActiveFindIndex: true);
    }

    /// <summary>
    /// Raised when the view needs image bytes.
    /// </summary>
    public event EventHandler<DictionaryResourceRequestedEventArgs>? ResourceRequested;

    /// <summary>
    /// Gets or sets the rich document to display.
    /// </summary>
    public DictionaryDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the anchor to scroll to and highlight.
    /// </summary>
    public string? HighlightedAnchor
    {
        get => GetValue(HighlightedAnchorProperty);
        set => SetValue(HighlightedAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets the current find query.
    /// </summary>
    public string FindText
    {
        get => GetValue(FindTextProperty);
        set => SetValue(FindTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the find status.
    /// </summary>
    public string FindStatus
    {
        get => GetValue(FindStatusProperty);
        set => SetValue(FindStatusProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom factor applied to rendered dictionary content.
    /// </summary>
    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>
    /// Gets or sets the base font size used by rendered dictionary content.
    /// </summary>
    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether example sentences are rendered in italic text.
    /// </summary>
    public bool IsExampleItalicEnabled
    {
        get => GetValue(IsExampleItalicEnabledProperty);
        set => SetValue(IsExampleItalicEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family used by example sentences.
    /// </summary>
    public FontFamily ExampleFontFamily
    {
        get => GetValue(ExampleFontFamilyProperty);
        set => SetValue(ExampleFontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the command used for dictionary navigation.
    /// </summary>
    public ICommand? NavigateCommand
    {
        get => GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command used for lookup links.
    /// </summary>
    public ICommand? LookupCommand
    {
        get => GetValue(LookupCommandProperty);
        set => SetValue(LookupCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command used for audio playback.
    /// </summary>
    public ICommand? PlayAudioCommand
    {
        get => GetValue(PlayAudioCommandProperty);
        set => SetValue(PlayAudioCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the web search engines shown for selected text.
    /// </summary>
    public IReadOnlyList<WebSearchSite>? WebSearchSites
    {
        get => GetValue(WebSearchSitesProperty);
        set => SetValue(WebSearchSitesProperty, value);
    }

    /// <summary>
    /// Moves to the next find hit.
    /// </summary>
    public void NextFindHit()
    {
        MoveFindHit(1);
    }

    /// <summary>
    /// Moves to the previous find hit.
    /// </summary>
    public void PreviousFindHit()
    {
        MoveFindHit(-1);
    }

    private void RenderDocument(bool resetScrollWhenNoAnchor, bool preserveActiveFindIndex = false)
    {
        UseNormalDocumentLayout();
        DocumentPanel.Children.Clear();
        _contentScrollViewers.Clear();
        _anchors.Clear();
        _findHits.Clear();
        _renderedFindHitIndex = 0;
        if (!preserveActiveFindIndex)
        {
            _activeFindIndex = -1;
        }

        if (Document is null)
        {
            FindStatus = string.Empty;
            if (resetScrollWhenNoAnchor)
            {
                ResetDocumentScroll();
            }

            return;
        }

        if (!TryRenderActivatorLayout(Document.Blocks) && !AddBlocksWithRightFloats(Document.Blocks))
        {
            foreach (DictionaryBlock block in Document.Blocks)
            {
                Control control = CreateBlockControl(block);
                DocumentPanel.Children.Add(control);
            }
        }

        FindStatus = _findHits.Count switch
        {
            0 when !string.IsNullOrWhiteSpace(FindText) => "No matches",
            0 => string.Empty,
            1 => "1 match",
            _ => _activeFindIndex >= 0 ? $"{_activeFindIndex + 1} of {_findHits.Count} matches" : $"{_findHits.Count} matches",
        };

        Dispatcher.UIThread.Post(
            () => ScrollAfterRender(resetScrollWhenNoAnchor),
            DispatcherPriority.Background);
    }

    private void UseNormalDocumentLayout()
    {
        if (!ReferenceEquals(ZoomTransformHost.Child, DocumentPanel))
        {
            ZoomTransformHost.Child = DocumentPanel;
        }

        DocumentScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        DocumentScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void UseActivatorDocumentLayout(Control content)
    {
        ZoomTransformHost.Child = content;
        DocumentScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        DocumentScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private bool TryRenderActivatorLayout(IReadOnlyList<DictionaryBlock> blocks)
    {
        int conceptIndex = -1;
        DictionaryContainerBlock? concept = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is DictionaryContainerBlock container && container.Style == DictionaryBlockStyle.ActivatorConcept)
            {
                conceptIndex = i;
                concept = container;
                break;
            }
        }

        if (concept is null)
        {
            return false;
        }

        List<DictionaryBlock> sectionBlocks = [];
        for (int i = 0; i < blocks.Count; i++)
        {
            if (i != conceptIndex)
            {
                sectionBlocks.Add(blocks[i]);
            }
        }

        UseActivatorDocumentLayout(CreateActivatorLayout(sectionBlocks, concept.Blocks));
        return true;
    }

    private Control CreateActivatorLayout(
        IReadOnlyList<DictionaryBlock> sectionBlocks,
        IReadOnlyList<DictionaryBlock> conceptBlocks)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(300)),
                new ColumnDefinition(GridLength.Star),
            },
        };

        Border conceptPane = new()
        {
            Child = CreateActivatorPane(conceptBlocks, new Thickness(0, 0, 10, 0), useActivatorConceptRows: true),
            BorderBrush = DocumentBorderBrushValue.Select(this),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        ScrollViewer sectionPane = CreateActivatorPane(sectionBlocks, new Thickness(15, 0, 15, 0), useActivatorConceptRows: false);

        grid.Children.Add(conceptPane);
        Grid.SetColumn(sectionPane, 1);
        grid.Children.Add(sectionPane);
        return grid;
    }

    private ScrollViewer CreateActivatorPane(
        IReadOnlyList<DictionaryBlock> blocks,
        Thickness margin,
        bool useActivatorConceptRows)
    {
        StackPanel panel = new()
        {
            Margin = margin,
            Spacing = 6,
        };
        foreach (DictionaryBlock block in blocks)
        {
            panel.Children.Add(useActivatorConceptRows
                ? CreateActivatorConceptBlockControl(block)
                : CreateBlockControl(block));
        }

        ScrollViewer scroller = new()
        {
            Content = panel,
            BringIntoViewOnFocusChange = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _contentScrollViewers.Add(scroller);
        return scroller;
    }

    private bool AddBlocksWithRightFloats(IReadOnlyList<DictionaryBlock> blocks)
    {
        Control? assetBox = null;
        List<Control> headImages = [];
        List<DictionaryBlock> flowBlocks = [];

        foreach (DictionaryBlock block in blocks)
        {
            if (block.Style == DictionaryBlockStyle.AssetBox)
            {
                assetBox = CreateBlockControl(block);
                ConfigureRightFloatControl(assetBox);
            }
            else if (block is DictionaryParagraphBlock paragraph && paragraph.Style == DictionaryBlockStyle.EntryHead)
            {
                flowBlocks.Add(ExtractRightFloatImages(paragraph, headImages));
            }
            else
            {
                flowBlocks.Add(block);
            }
        }

        int rightFloatCount = (assetBox is null ? 0 : 1) + headImages.Count;
        if (rightFloatCount == 0)
        {
            return false;
        }

        FloatingAssetPanel floatPanel = new(rightFloatCount);
        if (assetBox is not null)
        {
            floatPanel.Children.Add(assetBox);
        }

        foreach (Control headImage in headImages)
        {
            floatPanel.Children.Add(headImage);
        }

        foreach (DictionaryBlock block in flowBlocks)
        {
            floatPanel.Children.Add(CreateBlockControl(block));
        }

        DocumentPanel.Children.Add(floatPanel);
        return true;
    }

    private DictionaryParagraphBlock ExtractRightFloatImages(
        DictionaryParagraphBlock paragraph,
        List<Control> headImages)
    {
        List<DictionaryInline> retainedInlines = [];
        foreach (DictionaryInline inline in paragraph.Inlines)
        {
            if (inline is DictionaryImageInline image)
            {
                Control imageControl = CreateInlineImage(image);
                ConfigureRightFloatControl(imageControl);
                headImages.Add(imageControl);
            }
            else
            {
                retainedInlines.Add(inline);
            }
        }

        DictionaryParagraphBlock withoutImages = paragraph with { Inlines = retainedInlines };
        withoutImages.AnchorAliases = paragraph.AnchorAliases;
        return withoutImages;
    }

    private static void ConfigureRightFloatControl(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Top;
    }

    private Control CreateBlockControl(DictionaryBlock block)
    {
        Control control = CreateUntrackedBlockControl(block);
        RegisterBlockControl(block, control);
        return control;
    }

    private Control CreateUntrackedBlockControl(DictionaryBlock block)
    {
        return block switch
        {
            DictionaryHeadingBlock heading => CreateHeading(heading),
            DictionaryParagraphBlock paragraph => CreateParagraph(paragraph),
            DictionaryContainerBlock container => CreateContainer(container),
            DictionaryImageBlock image => CreateImageBlock(image),
            _ => new TextBlock(),
        };
    }

    private void RegisterBlockControl(DictionaryBlock block, Control control)
    {
        if (!string.IsNullOrWhiteSpace(block.Anchor))
        {
            _anchors[block.Anchor] = control;
        }

        foreach (string alias in block.AnchorAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _anchors.TryAdd(alias, control);
            }
        }

        if (!string.IsNullOrWhiteSpace(HighlightedAnchor)
            && (string.Equals(block.Anchor, HighlightedAnchor, StringComparison.Ordinal)
                || block.AnchorAliases.Contains(HighlightedAnchor, StringComparer.Ordinal)))
        {
            ApplyHighlightBackground(control);
        }
    }

    private Control CreateActivatorConceptBlockControl(DictionaryBlock block)
    {
        Control control = block is DictionaryParagraphBlock paragraph
            && TryCreateActivatorConceptLinkRow(paragraph, out Control? linkRow)
                ? linkRow
                : CreateUntrackedBlockControl(block);
        RegisterBlockControl(block, control);
        return control;
    }

    private bool TryCreateActivatorConceptLinkRow(DictionaryParagraphBlock paragraph, out Control control)
    {
        control = new TextBlock();
        if (paragraph.Inlines.Count != 1
            || paragraph.Inlines[0] is not DictionaryLinkInline link
            || link.Inlines.Any(ContainsInteractiveInline))
        {
            return false;
        }

        TextBlock content = new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = GetFontSize(paragraph.Style),
            Foreground = DocumentLinkForegroundBrush.Select(this),
            Inlines = [],
        };
        if (paragraph.Style == DictionaryBlockStyle.ActivatorSection)
        {
            content.FontWeight = FontWeight.SemiBold;
        }

        AddInlines(content.Inlines!, link.Inlines, content);
        SetLinkForeground(content);

        Button button = new()
        {
            Content = content,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DocumentLinkForegroundBrush.Select(this),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => ExecuteLink(link.Target);

        if (paragraph.Style != DictionaryBlockStyle.ActivatorSection)
        {
            button.Margin = GetBlockMargin(paragraph.Style);
            control = button;
            return true;
        }

        control = new Border
        {
            Child = button,
            Padding = new Thickness(6, 3),
            Margin = GetBlockMargin(paragraph.Style),
            BorderThickness = new Thickness(1),
            BorderBrush = ActivatorSectionBorderBrushValue.Select(this),
            Background = ActivatorSectionBackgroundBrush.Select(this),
        };
        return true;
    }

    private static bool ContainsInteractiveInline(DictionaryInline inline)
    {
        return inline is DictionaryLinkInline or DictionaryAudioInline or DictionaryImageInline;
    }

    /// <summary>
    /// Handles changes to document rendering properties.
    /// </summary>
    /// <param name="change">Property change details.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            RenderDocument(resetScrollWhenNoAnchor: true);
        }
        else if (change.Property == FindTextProperty)
        {
            RenderDocument(resetScrollWhenNoAnchor: false);
        }
        else if (change.Property == ZoomFactorProperty)
        {
            ApplyZoomFactor();
        }
        else if (change.Property == BaseFontSizeProperty ||
            change.Property == IsExampleItalicEnabledProperty ||
            change.Property == ExampleFontFamilyProperty)
        {
            RenderDocument(resetScrollWhenNoAnchor: false, preserveActiveFindIndex: true);
        }
    }

    private void ApplyZoomFactor()
    {
        double zoomFactor = ZoomFactor <= 0 ? 1.0 : ZoomFactor;
        ZoomTransformHost.LayoutTransform = new ScaleTransform(zoomFactor, zoomFactor);
    }

    private Control CreateHeading(DictionaryHeadingBlock heading)
    {
        return CreateInlineTextControl(
            DictionaryBlockStyle.Heading,
            heading.Inlines,
            textBlock =>
            {
                textBlock.FontSize = ScaleFontSize(heading.Level == 1 ? 28 : 20);
                textBlock.FontWeight = FontWeight.SemiBold;
            });
    }

    private Control CreateParagraph(DictionaryParagraphBlock paragraph)
    {
        if (paragraph.Style == DictionaryBlockStyle.BoxHeading)
        {
            TextBlock heading = CreateTextBlock(paragraph.Style);
            heading.Margin = new Thickness(0);
            AddInlines(heading.Inlines!, paragraph.Inlines, heading);
            return new Border
            {
                Child = heading,
                Padding = new Thickness(6, 2),
                Margin = GetBlockMargin(paragraph.Style),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(BoxHeadingGradientStartColor, 0),
                        new GradientStop(BoxHeadingGradientEndColor, 1),
                    },
                },
            };
        }

        Control content = CreateInlineTextControl(paragraph.Style, paragraph.Inlines);
        if (paragraph.Style == DictionaryBlockStyle.ActivatorSection)
        {
            content.Margin = new Thickness(0);
            if (content is TextBlock textBlock)
            {
                textBlock.FontWeight = FontWeight.SemiBold;
            }

            return new Border
            {
                Child = content,
                Padding = new Thickness(6, 3),
                Margin = GetBlockMargin(paragraph.Style),
                BorderThickness = new Thickness(1),
                BorderBrush = ActivatorSectionBorderBrushValue.Select(this),
                Background = ActivatorSectionBackgroundBrush.Select(this),
            };
        }

        return content;
    }

    private Border CreateContainer(DictionaryContainerBlock container)
    {
        Border border = new()
        {
            Child = CreateContainerContent(container),
            Padding = GetContainerPadding(container.Style),
            Margin = GetBlockMargin(container.Style),
            BorderThickness = GetContainerBorderThickness(container.Style),
            BorderBrush = DocumentBorderBrushValue.Select(this),
            Background = GetContainerBackground(container.Style),
        };
        if (container.Style == DictionaryBlockStyle.AssetBox)
        {
            border.HorizontalAlignment = HorizontalAlignment.Right;
            border.MaxWidth = 200;
        }

        return border;
    }

    private Control CreateContainerContent(DictionaryContainerBlock container)
    {
        if (container.Style != DictionaryBlockStyle.Sense)
        {
            return CreateStackedBlocks(container.Blocks);
        }

        List<Control> rightFloats = [];
        List<DictionaryBlock> flowBlocks = [];
        foreach (DictionaryBlock block in container.Blocks)
        {
            if (block is DictionaryImageBlock)
            {
                Control image = CreateBlockControl(block);
                ConfigureRightFloatControl(image);
                rightFloats.Add(image);
            }
            else
            {
                flowBlocks.Add(block);
            }
        }

        if (rightFloats.Count == 0)
        {
            return CreateStackedBlocks(container.Blocks);
        }

        flowBlocks = MergeLeadingSenseMarker(flowBlocks);

        FloatingAssetPanel panel = new(rightFloats.Count);
        foreach (Control rightFloat in rightFloats)
        {
            panel.Children.Add(rightFloat);
        }

        foreach (DictionaryBlock block in flowBlocks)
        {
            panel.Children.Add(CreateBlockControl(block));
        }

        return panel;
    }

    private Control CreateStackedBlocks(IReadOnlyList<DictionaryBlock> blocks)
    {
        StackPanel panel = new() { Spacing = 6 };
        foreach (DictionaryBlock child in blocks)
        {
            panel.Children.Add(CreateBlockControl(child));
        }

        return panel;
    }

    private static List<DictionaryBlock> MergeLeadingSenseMarker(IReadOnlyList<DictionaryBlock> blocks)
    {
        if (blocks.Count < 2
            || blocks[0] is not DictionaryParagraphBlock marker
            || blocks[1] is not DictionaryParagraphBlock definition
            || marker.Style != DictionaryBlockStyle.Normal
            || definition.Style != DictionaryBlockStyle.Normal
            || !IsSenseMarkerParagraph(marker))
        {
            return blocks.ToList();
        }

        List<DictionaryInline> inlines = [.. marker.Inlines, new DictionaryTextInline(" ", DictionaryTextStyle.Normal), .. definition.Inlines];
        string? anchor = marker.Anchor ?? definition.Anchor;
        DictionaryParagraphBlock merged = new(inlines, marker.Style, anchor)
        {
            AnchorAliases = MergeAnchorAliases(marker, definition, anchor),
        };

        List<DictionaryBlock> mergedBlocks = [merged];
        mergedBlocks.AddRange(blocks.Skip(2));
        return mergedBlocks;
    }

    private static bool IsSenseMarkerParagraph(DictionaryParagraphBlock paragraph)
    {
        foreach (DictionaryInline inline in paragraph.Inlines)
        {
            if (inline is DictionaryAudioInline)
            {
                continue;
            }

            if (inline is DictionaryTextInline text
                && (string.IsNullOrWhiteSpace(text.Text)
                    || text.Style == DictionaryTextStyle.Badge && text.Text.Trim().All(char.IsDigit)))
            {
                continue;
            }

            return false;
        }

        return paragraph.Inlines.Count > 0;
    }

    private static IReadOnlyList<string> MergeAnchorAliases(
        DictionaryParagraphBlock first,
        DictionaryParagraphBlock second,
        string? anchor)
    {
        List<string> aliases = [.. first.AnchorAliases];
        if (!string.IsNullOrWhiteSpace(second.Anchor) && !string.Equals(second.Anchor, anchor, StringComparison.Ordinal))
        {
            aliases.Add(second.Anchor);
        }

        aliases.AddRange(second.AnchorAliases);
        return aliases.Distinct(StringComparer.Ordinal).ToArray();
    }

    private Control CreateImageBlock(DictionaryImageBlock image)
    {
        Image imageControl = new()
        {
            Source = LoadBitmap(image.Resource),
            Stretch = Stretch.Uniform,
            Width = 150,
            MaxHeight = 150,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Border imageFrame = new()
        {
            Child = imageControl,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(4),
            Margin = new Thickness(12, 0, 0, 8),
            BorderBrush = DocumentBorderBrushValue.Select(this),
            BorderThickness = new Thickness(1),
            Background = ImageFrameBackgroundBrush.Select(this),
        };

        Control content;
        if (string.IsNullOrWhiteSpace(image.Caption))
        {
            content = imageFrame;
        }
        else
        {
            content = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children =
                {
                    imageFrame,
                    new TextBlock
                    {
                        Text = image.Caption,
                        FontSize = ScaleFontSize(12),
                        Foreground = DocumentMutedForegroundBrush.Select(this),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
        }

        return image.Target is null ? content : CreateImageButton(content, image.Target);
    }

    private Control CreateInlineTextControl(
        DictionaryBlockStyle style,
        IReadOnlyList<DictionaryInline> inlines,
        Action<TextBlock>? configureTextBlock = null)
    {
        int leadingAdornmentCount = CountLeadingAdornments(inlines);
        if (leadingAdornmentCount == 0 || leadingAdornmentCount >= inlines.Count)
        {
            TextBlock textBlock = CreateTextBlock(style);
            configureTextBlock?.Invoke(textBlock);
            AddInlines(textBlock.Inlines!, inlines, textBlock);
            return textBlock;
        }

        TextBlock trailingTextBlock = CreateTextBlock(style);
        trailingTextBlock.Margin = new Thickness(0);
        configureTextBlock?.Invoke(trailingTextBlock);
        AddInlines(trailingTextBlock.Inlines!, inlines.Skip(leadingAdornmentCount).ToArray(), trailingTextBlock);

        StackPanel leadingPanel = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
        };
        foreach (DictionaryInline inline in inlines.Take(leadingAdornmentCount))
        {
            Control? control = CreateLeadingAdornmentControl(inline);
            if (control is not null)
            {
                leadingPanel.Children.Add(control);
            }
        }

        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Margin = GetBlockMargin(style),
        };
        grid.Children.Add(leadingPanel);
        Grid.SetColumn(trailingTextBlock, 1);
        grid.Children.Add(trailingTextBlock);
        return grid;
    }

    private TextBlock CreateTextBlock(DictionaryBlockStyle style)
    {
        SelectableTextBlock textBlock = new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = GetFontSize(style),
            Margin = GetBlockMargin(style),
            Inlines = [],
        };
        AttachSelectionContextMenu(textBlock);

        if (style == DictionaryBlockStyle.Example)
        {
            textBlock.FontFamily = ExampleFontFamily;
            textBlock.FontStyle = IsExampleItalicEnabled ? FontStyle.Italic : FontStyle.Normal;
            textBlock.Margin = new Thickness(18, 1, 0, 1);
        }
        else if (style == DictionaryBlockStyle.BoxHeading)
        {
            textBlock.FontSize = ScaleFontSize(14);
            textBlock.FontWeight = FontWeight.Bold;
            textBlock.Foreground = DocumentTagForeground;
        }

        return textBlock;
    }

    private static int CountLeadingAdornments(IReadOnlyList<DictionaryInline> inlines)
    {
        int count = 0;
        while (count < inlines.Count)
        {
            DictionaryInline inline = inlines[count];
            if (IsLeadingAdornment(inline)
                || (count > 0 && inline is DictionaryTextInline text && string.IsNullOrWhiteSpace(text.Text)))
            {
                count++;
                continue;
            }

            break;
        }

        return count;
    }

    private static bool IsLeadingAdornment(DictionaryInline inline)
    {
        return inline is DictionaryAudioInline
            or DictionaryImageInline
            || inline is DictionaryTextInline text && IsDecoratedTagStyle(text.Style);
    }

    private Control? CreateLeadingAdornmentControl(DictionaryInline inline)
    {
        return inline switch
        {
            DictionaryAudioInline audio => CreateAudioButton(audio),
            DictionaryImageInline image => CreateInlineImage(image),
            DictionaryTextInline text when IsDecoratedTagStyle(text.Style) =>
                CreateDecoratedTagControl(text.Text.Trim(), text.Style, isFindHit: false, isActiveFindHit: false, isAnchorHighlight: false),
            DictionaryTextInline text => new TextBlock { Text = text.Text },
            _ => null,
        };
    }

    private void AttachSelectionContextMenu(SelectableTextBlock textBlock)
    {
        MenuItem lookupItem = new()
        {
            Header = "Lookup",
            InputGesture = LookupSelectedTextGesture,
            IsEnabled = false,
        };
        MenuItem copyItem = new()
        {
            Header = "Copy",
            InputGesture = CopySelectedTextGesture,
            IsEnabled = false,
        };
        MenuItem webSearchItem = new()
        {
            Header = "Web Search",
            IsEnabled = false,
        };

        ContextMenu contextMenu = new();
        contextMenu.Items.Add(lookupItem);
        contextMenu.Items.Add(webSearchItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(copyItem);

        textBlock.KeyDown += (_, e) =>
        {
            if (LookupSelectedTextGesture.Matches(e))
            {
                e.Handled = LookupSelectedText(textBlock);
            }
        };

        contextMenu.Opening += (_, _) =>
        {
            string selectedText = GetSelectedText(textBlock);
            bool hasSelection = selectedText.Length > 0;
            lookupItem.Header = hasSelection
                ? $"Lookup \"{Ellipsize(selectedText.ToLowerInvariant(), 12)}\""
                : "Lookup";
            lookupItem.IsEnabled = hasSelection && LookupCommand?.CanExecute(selectedText) == true;
            webSearchItem.Items.Clear();
            if (hasSelection)
            {
                foreach (WebSearchLink webSearchLink in WebSearchLinks.Create(selectedText, WebSearchSites))
                {
                    MenuItem siteItem = new()
                    {
                        Header = webSearchLink.Title,
                    };
                    siteItem.Click += (_, _) =>
                    {
                        _ = LaunchExternalLinkAsync(webSearchLink.Url);
                    };
                    webSearchItem.Items.Add(siteItem);
                }
            }

            webSearchItem.IsEnabled = hasSelection && webSearchItem.Items.Count > 0;
            copyItem.IsEnabled = hasSelection && textBlock.CanCopy;
        };

        lookupItem.Click += (_, _) =>
        {
            LookupSelectedText(textBlock);
        };

        copyItem.Click += (_, _) =>
        {
            if (textBlock.CanCopy)
            {
                textBlock.Copy();
            }
        };

        textBlock.ContextMenu = contextMenu;
    }

    private bool LookupSelectedText(SelectableTextBlock textBlock)
    {
        string selectedText = GetSelectedText(textBlock);
        if (selectedText.Length == 0 || LookupCommand?.CanExecute(selectedText) != true)
        {
            return false;
        }

        LookupCommand.Execute(selectedText);
        return true;
    }

    private static string GetSelectedText(SelectableTextBlock textBlock)
    {
        return textBlock.SelectedText.Trim();
    }

    private static string Ellipsize(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private void AddInlines(InlineCollection target, IReadOnlyList<DictionaryInline> inlines, Control owner)
    {
        foreach (DictionaryInline inline in inlines)
        {
            switch (inline)
            {
                case DictionaryTextInline text:
                    AddTextRuns(target, text, owner);
                    break;
                case DictionaryLineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case DictionaryLinkInline link:
                    target.Add(new InlineUIContainer(CreateLinkButton(link))
                    {
                        BaselineAlignment = BaselineAlignment.Bottom
                    });
                    break;
                case DictionaryAudioInline audio:
                    target.Add(new InlineUIContainer(CreateAudioButton(audio))
                    {
                        BaselineAlignment = BaselineAlignment.Center
                    });
                    break;
                case DictionaryImageInline image:
                    target.Add(new InlineUIContainer(CreateInlineImage(image)));
                    break;
            }
        }
    }

    private void AddTextRuns(InlineCollection target, DictionaryTextInline text, Control owner)
    {
        bool isAnchorHighlight = !string.IsNullOrWhiteSpace(text.Anchor)
            && string.Equals(text.Anchor, HighlightedAnchor, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(text.Anchor))
        {
            _anchors.TryAdd(text.Anchor, owner);
        }

        string query = FindText.Trim();
        if (IsDecoratedTagStyle(text.Style))
        {
            AddDecoratedTag(target, text, query, owner, isAnchorHighlight);
            return;
        }

        if (query.Length == 0)
        {
            target.Add(CreateRun(text.Text, text.Style, isFindHit: false, isActiveFindHit: false, isAnchorHighlight));
            return;
        }

        int offset = 0;
        while (offset < text.Text.Length)
        {
            int index = text.Text.IndexOf(query, offset, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0)
            {
                target.Add(CreateRun(text.Text[offset..], text.Style, isFindHit: false, isActiveFindHit: false, isAnchorHighlight));
                break;
            }

            if (index > offset)
            {
                target.Add(CreateRun(text.Text[offset..index], text.Style, isFindHit: false, isActiveFindHit: false, isAnchorHighlight));
            }

            int hitIndex = _renderedFindHitIndex++;
            bool isActiveFindHit = hitIndex == _activeFindIndex;
            target.Add(CreateRun(text.Text.Substring(index, query.Length), text.Style, isFindHit: true, isActiveFindHit, isAnchorHighlight));
            _findHits.Add(owner);
            offset = index + query.Length;
        }
    }

    private void AddDecoratedTag(
        InlineCollection target,
        DictionaryTextInline text,
        string query,
        Control owner,
        bool isAnchorHighlight)
    {
        string value = text.Text;
        int start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        int end = value.Length;
        while (end > start && char.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        if (start > 0)
        {
            target.Add(CreateRun(value[..start], DictionaryTextStyle.Normal, isFindHit: false, isActiveFindHit: false, isAnchorHighlight: false));
        }

        if (end > start)
        {
            string coreText = value[start..end];
            bool isFindHit = query.Length > 0
                && coreText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            bool isActiveFindHit = false;
            if (isFindHit)
            {
                int hitIndex = _renderedFindHitIndex++;
                isActiveFindHit = hitIndex == _activeFindIndex;
                _findHits.Add(owner);
            }

            target.Add(new InlineUIContainer(CreateDecoratedTagControl(coreText, text.Style, isFindHit, isActiveFindHit, isAnchorHighlight))
            {
                BaselineAlignment = BaselineAlignment.Center
            });
        }

        if (end < value.Length)
        {
            target.Add(CreateRun(value[end..], DictionaryTextStyle.Normal, isFindHit: false, isActiveFindHit: false, isAnchorHighlight: false));
        }
    }

    private Border CreateDecoratedTagControl(
        string text,
        DictionaryTextStyle style,
        bool isFindHit,
        bool isActiveFindHit,
        bool isAnchorHighlight)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            FontSize = ScaleFontSize(12),
            FontWeight = FontWeight.Bold,
            Foreground = DocumentTagForeground,
        };

        IBrush background = style switch
        {
            DictionaryTextStyle.RelationTag => RelationTagBackground,
            DictionaryTextStyle.FrequencyTag => FrequencyTagBackground,
            _ => SignpostTagBackground,
        };

        if (isAnchorHighlight)
        {
            background = HighlightBackgroundBrush.Select(this);
            textBlock.Foreground = HighlightForegroundBrush.Select(this);
        }
        else if (isActiveFindHit)
        {
            background = ActiveFindBackgroundBrush.Select(this);
            textBlock.Foreground = HighlightForegroundBrush.Select(this);
        }
        else if (isFindHit)
        {
            background = FindBackgroundBrush.Select(this);
            textBlock.Foreground = HighlightForegroundBrush.Select(this);
        }

        return new Border
        {
            Child = textBlock,
            Background = background,
            Padding = new Thickness(4, 1),
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static bool IsDecoratedTagStyle(DictionaryTextStyle style)
    {
        return style is DictionaryTextStyle.Signpost or DictionaryTextStyle.RelationTag or DictionaryTextStyle.FrequencyTag;
    }

    private Run CreateRun(string text, DictionaryTextStyle style, bool isFindHit, bool isActiveFindHit, bool isAnchorHighlight)
    {
        Run run = new(text);
        switch (style)
        {
            case DictionaryTextStyle.Headword:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = DocumentHeadwordForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Hyphenation:
                run.FontSize = ScaleFontSize(26);
                run.FontWeight = FontWeight.Bold;
                break;
            case DictionaryTextStyle.HomonymNumber:
                run.BaselineAlignment = BaselineAlignment.Superscript;
                run.FontSize = ScaleFontSize(16);
                run.FontWeight = FontWeight.Bold;
                break;
            case DictionaryTextStyle.Label:
                run.FontStyle = FontStyle.Italic;
                run.Foreground = DocumentLinkForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.PartOfSpeech:
                run.FontSize = ScaleFontSize(13);
                run.FontStyle = FontStyle.Italic;
                run.Foreground = DocumentMutedForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Definition:
                run.Foreground = DocumentForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Example:
                run.FontFamily = ExampleFontFamily;
                run.FontStyle = IsExampleItalicEnabled ? FontStyle.Italic : FontStyle.Normal;
                break;
            case DictionaryTextStyle.ExampleStrong:
                run.FontFamily = ExampleFontFamily;
                run.FontStyle = IsExampleItalicEnabled ? FontStyle.Italic : FontStyle.Normal;
                run.FontWeight = FontWeight.Bold;
                break;
            case DictionaryTextStyle.Emphasis:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case DictionaryTextStyle.AssetTitle:
                run.FontStyle = FontStyle.Italic;
                run.FontWeight = FontWeight.SemiBold;
                break;
            case DictionaryTextStyle.WordFamilyPartOfSpeech:
                run.FontStyle = FontStyle.Italic;
                run.FontWeight = FontWeight.Bold;
                break;
            case DictionaryTextStyle.Strong:
                run.FontWeight = FontWeight.SemiBold;
                run.Foreground = DocumentHeadwordForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Origin:
                run.Foreground = DocumentGreenForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Muted:
                run.Foreground = DocumentMutedForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Badge:
                run.FontWeight = FontWeight.Bold;
                run.Foreground = DocumentBadgeForegroundBrush.Select(this);
                break;
            case DictionaryTextStyle.Signpost:
                run.FontWeight = FontWeight.Bold;
                run.FontSize = ScaleFontSize(12);
                run.Foreground = DocumentTagForeground;
                run.Background = SignpostTagBackground;
                break;
            case DictionaryTextStyle.RelationTag:
                run.FontWeight = FontWeight.Bold;
                run.FontSize = ScaleFontSize(12);
                run.Foreground = DocumentTagForeground;
                run.Background = RelationTagBackground;
                break;
            case DictionaryTextStyle.FrequencyTag:
                run.FontWeight = FontWeight.Bold;
                run.FontSize = ScaleFontSize(12);
                run.Foreground = DocumentTagForeground;
                run.Background = FrequencyTagBackground;
                break;
        }

        if (isAnchorHighlight)
        {
            run.Background = HighlightBackgroundBrush.Select(this);
            run.Foreground = HighlightForegroundBrush.Select(this);
        }
        else if (isActiveFindHit)
        {
            run.Background = ActiveFindBackgroundBrush.Select(this);
            run.Foreground = HighlightForegroundBrush.Select(this);
        }
        else if (isFindHit)
        {
            run.Background = FindBackgroundBrush.Select(this);
            run.Foreground = HighlightForegroundBrush.Select(this);
        }

        return run;
    }

    private Button CreateLinkButton(DictionaryLinkInline link)
    {
        TextBlock content = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = DocumentLinkForegroundBrush.Select(this),
            Inlines = [],
        };
        AddInlines(content.Inlines!, link.Inlines, content);
        SetLinkForeground(content);

        Button button = new()
        {
            Content = content,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DocumentLinkForegroundBrush.Select(this),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => ExecuteLink(link.Target);
        return button;
    }

    private void SetLinkForeground(TextBlock textBlock)
    {
        textBlock.Foreground = DocumentLinkForegroundBrush.Select(this);
        if (textBlock.Inlines is null)
        {
            return;
        }

        foreach (Inline inline in textBlock.Inlines)
        {
            if (inline is not Run run)
            {
                continue;
            }

            run.Foreground = DocumentLinkForegroundBrush.Select(this);
        }
    }

    private Button CreateAudioButton(DictionaryAudioInline audio)
    {
        IBrush iconBrush = GetAudioIconBrush(audio.Resource.Archive);
        double iconSize = ScaleFontSize(16);
        double buttonSize = ScaleFontSize(20);
        double padding = ScaleFontSize(2);
        double horizontalMargin = ScaleFontSize(3);
        PathIcon icon = new()
        {
            Data = GetAudioIconGeometry(audio.Resource.Archive),
            Foreground = iconBrush,
            Width = iconSize,
            Height = iconSize,
        };

        Button button = new()
        {
            Content = icon,
            Width = buttonSize,
            Height = buttonSize,
            Padding = new Thickness(padding),
            Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = iconBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Command = PlayAudioCommand,
            CommandParameter = audio.Resource,
        };
        ToolTip.SetTip(button, audio.Title);
        return button;
    }

    private Geometry GetAudioIconGeometry(string archive)
    {
        string resourceKey = IsPronunciationAudio(archive) ? "speaker_filled" : "play_circle_regular";
        return (Geometry)this.FindResource(resourceKey)!;
    }

    private IBrush GetAudioIconBrush(string archive)
    {
        return archive switch
        {
            "gb_hwd_pron" => BritishAudioIconBrush.Select(this),
            "us_hwd_pron" => AmericanAudioIconBrush.Select(this),
            _ => DefaultAudioIconBrush.Select(this),
        };
    }

    private static bool IsPronunciationAudio(string archive)
    {
        return archive is "gb_hwd_pron" or "us_hwd_pron";
    }

    private Control CreateInlineImage(DictionaryImageInline image)
    {
        Image imageControl = new()
        {
            Source = LoadBitmap(image.Resource),
            Width = 120,
            MaxHeight = 120,
            Stretch = Stretch.Uniform,
        };

        if (image.Target is null)
        {
            return imageControl;
        }

        return CreateImageButton(imageControl, image.Target);
    }

    private Button CreateImageButton(Control content, DictionaryLinkTarget target)
    {
        Button button = new()
        {
            Content = content,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, "Open full-size image");
        button.Click += (_, _) => ExecuteLink(target);
        return button;
    }

    private void ExecuteLink(DictionaryLinkTarget target)
    {
        if (target.Kind == DictionaryLinkTargetKind.Image && ShowImageTarget(target))
        {
            return;
        }

        if (target.Kind == DictionaryLinkTargetKind.External)
        {
            _ = LaunchExternalLinkAsync(target.Value);
            return;
        }

        ICommand? command = target.Kind switch
        {
            DictionaryLinkTargetKind.Lookup => LookupCommand,
            _ => NavigateCommand,
        };

        if (command?.CanExecute(target.Value) == true)
        {
            command.Execute(target.Value);
        }
    }

    private async Task LaunchExternalLinkAsync(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        await (TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri) ?? Task.CompletedTask);
    }

    private bool ShowImageTarget(DictionaryLinkTarget target)
    {
        DictionaryResourceRef? resource = TryCreatePictureResource(target);
        if (resource is null)
        {
            return false;
        }

        Bitmap? bitmap = LoadBitmap(resource);
        if (bitmap is null)
        {
            return false;
        }

        Window imageWindow = CreateImageWindow(bitmap, resource.Name);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            imageWindow.Show(owner);
        }
        else
        {
            imageWindow.Show();
        }

        return true;
    }

    private Window CreateImageWindow(Bitmap bitmap, string resourceName)
    {
        Window? owner = TopLevel.GetTopLevel(this) as Window;
        double ownerWidth = owner?.Bounds.Width > 0 ? owner.Bounds.Width : 900;
        double ownerHeight = owner?.Bounds.Height > 0 ? owner.Bounds.Height : 700;
        Window window = new()
        {
            Title = Path.GetFileName(resourceName),
            Width = Math.Clamp(ownerWidth * 0.82, 420, 1100),
            Height = Math.Clamp(ownerHeight * 0.82, 320, 850),
            MinWidth = 360,
            MinHeight = 260,
            RequestedThemeVariant = ActualThemeVariant,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };

        Grid header = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Margin = new Thickness(12, 10, 12, 0),
        };
        TextBlock title = new()
        {
            Text = Path.GetFileName(resourceName),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Button closeButton = new()
        {
            Content = "Close",
            Padding = new Thickness(12, 4),
        };
        closeButton.Click += (_, _) => window.Close();
        header.Children.Add(title);
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);

        Image image = new()
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Border imageHost = new()
        {
            Child = image,
            Padding = new Thickness(12),
        };

        root.Children.Add(header);
        Grid.SetRow(imageHost, 1);
        root.Children.Add(imageHost);
        window.Content = root;
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        return window;
    }

    private static DictionaryResourceRef? TryCreatePictureResource(DictionaryLinkTarget target)
    {
        if (target.Kind != DictionaryLinkTargetKind.Image)
        {
            return null;
        }

        string[] parts = target.Value.Trim('/').Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "picture", StringComparison.Ordinal))
        {
            return null;
        }

        return new DictionaryResourceRef("picture", parts[1] + "/" + parts[2], "image/jpeg");
    }

    private Bitmap? LoadBitmap(DictionaryResourceRef resource)
    {
        if (_imageCache.TryGetValue(resource, out Bitmap? cached))
        {
            return cached;
        }

        DictionaryResourceRequestedEventArgs args = new(resource);
        ResourceRequested?.Invoke(this, args);
        if (args.Data is null)
        {
            return null;
        }

        Bitmap bitmap;
        try
        {
            using MemoryStream stream = new(args.Data);
            bitmap = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return null;
        }

        if (_imageCache.Count > 24)
        {
            DictionaryResourceRef first = _imageCache.Keys.First();
            _imageCache[first].Dispose();
            _imageCache.Remove(first);
        }

        _imageCache[resource] = bitmap;
        return bitmap;
    }

    private void ScrollAfterRender(bool resetScrollWhenNoAnchor)
    {
        if (!ScrollToHighlightedAnchor() && resetScrollWhenNoAnchor)
        {
            ResetDocumentScroll();
        }
    }

    private bool ScrollToHighlightedAnchor()
    {
        if (string.IsNullOrWhiteSpace(HighlightedAnchor)
            || !_anchors.TryGetValue(HighlightedAnchor, out Control? control))
        {
            return false;
        }

        CenterControlInDocumentScroller(control);
        return true;
    }

    private void ResetDocumentScroll()
    {
        DocumentScroller.Offset = new Vector(DocumentScroller.Offset.X, 0);
        foreach (ScrollViewer scroller in _contentScrollViewers)
        {
            scroller.Offset = new Vector(scroller.Offset.X, 0);
        }
    }

    private void CenterControlInDocumentScroller(Control control)
    {
        ScrollViewer? scroller = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is null)
        {
            control.BringIntoView();
            return;
        }

        CenterControlInScroller(control, scroller);
    }

    private static void CenterControlInScroller(Control control, ScrollViewer scroller)
    {
        if (scroller.Content is not Control content || scroller.Viewport.Height <= 0)
        {
            control.BringIntoView();
            return;
        }

        Point? point = control.TranslatePoint(new Point(0, 0), content);
        if (point is null)
        {
            control.BringIntoView();
            return;
        }

        double targetY = point.Value.Y - ((scroller.Viewport.Height - control.Bounds.Height) / 2);
        double maxY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        double centeredY = Math.Clamp(targetY, 0, maxY);
        scroller.Offset = new Vector(scroller.Offset.X, centeredY);
    }

    private void MoveFindHit(int delta)
    {
        if (_findHits.Count == 0)
        {
            FindStatus = string.IsNullOrWhiteSpace(FindText) ? string.Empty : "No matches";
            return;
        }

        _activeFindIndex = _activeFindIndex < 0
            ? (delta > 0 ? 0 : _findHits.Count - 1)
            : (_activeFindIndex + delta + _findHits.Count) % _findHits.Count;

        RenderDocument(resetScrollWhenNoAnchor: false, preserveActiveFindIndex: true);
        if (_findHits.Count == 0)
        {
            _activeFindIndex = -1;
            FindStatus = string.IsNullOrWhiteSpace(FindText) ? string.Empty : "No matches";
            return;
        }

        if (_activeFindIndex >= _findHits.Count)
        {
            _activeFindIndex = _findHits.Count - 1;
            RenderDocument(resetScrollWhenNoAnchor: false, preserveActiveFindIndex: true);
        }

        int activeFindIndex = _activeFindIndex;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (activeFindIndex >= 0
                    && activeFindIndex == _activeFindIndex
                    && activeFindIndex < _findHits.Count)
                {
                    CenterControlInDocumentScroller(_findHits[activeFindIndex]);
                }
            },
            DispatcherPriority.Background);
        FindStatus = $"{_activeFindIndex + 1} of {_findHits.Count} matches";
    }

    private static Thickness GetBlockMargin(DictionaryBlockStyle style)
    {
        return style switch
        {
            DictionaryBlockStyle.EntryHead => new Thickness(0, 0, 0, 10),
            DictionaryBlockStyle.Sense => new Thickness(0, 4, 0, 6),
            DictionaryBlockStyle.BoxHeading => new Thickness(0, 0, 0, 4),
            DictionaryBlockStyle.Box => new Thickness(0, 8, 0, 8),
            DictionaryBlockStyle.AssetBox => new Thickness(0, 0, 0, 0),
            _ => new Thickness(0, 2, 0, 2),
        };
    }

    private double GetFontSize(DictionaryBlockStyle style)
    {
        double fontSize = style switch
        {
            DictionaryBlockStyle.EntryHead => 15,
            DictionaryBlockStyle.AssetBox => 13,
            _ => 15,
        };
        return ScaleFontSize(fontSize);
    }

    private double ScaleFontSize(double fontSize)
    {
        double baseFontSize = BaseFontSize <= 0 ? DefaultBaseFontSize : BaseFontSize;
        return fontSize * baseFontSize / DefaultBaseFontSize;
    }

    private static Thickness GetContainerPadding(DictionaryBlockStyle style)
    {
        return style is DictionaryBlockStyle.Box or DictionaryBlockStyle.AssetBox
            ? new Thickness(10, 8)
            : new Thickness(0);
    }

    private static Thickness GetContainerBorderThickness(DictionaryBlockStyle style)
    {
        return style is DictionaryBlockStyle.Box or DictionaryBlockStyle.AssetBox
            ? new Thickness(1)
            : new Thickness(0);
    }

    private IBrush? GetContainerBackground(DictionaryBlockStyle style)
    {
        return style switch
        {
            DictionaryBlockStyle.AssetBox => DocumentPanelBackgroundBrush.Select(this),
            DictionaryBlockStyle.Box => DocumentPanelAltBackgroundBrush.Select(this),
            DictionaryBlockStyle.ActivatorSection => DocumentPanelBackgroundBrush.Select(this),
            _ => null,
        };
    }

    private void ApplyHighlightBackground(Control control)
    {
        switch (control)
        {
            case TextBlock textBlock:
                textBlock.Background = HighlightBackgroundBrush.Select(this);
                textBlock.Foreground = HighlightForegroundBrush.Select(this);
                break;
            case Border border:
                border.Background = HighlightBackgroundBrush.Select(this);
                break;
            case Panel panel:
                panel.Background = HighlightBackgroundBrush.Select(this);
                break;
        }
    }
}

/// <summary>
/// Resource request event for dictionary images.
/// </summary>
public sealed class DictionaryResourceRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Creates resource request event data.
    /// </summary>
    /// <param name="resource">Requested resource.</param>
    public DictionaryResourceRequestedEventArgs(DictionaryResourceRef resource)
    {
        Resource = resource;
    }

    /// <summary>
    /// Gets the requested resource.
    /// </summary>
    public DictionaryResourceRef Resource { get; }

    /// <summary>
    /// Gets or sets loaded resource bytes.
    /// </summary>
    public byte[]? Data { get; set; }
}

internal sealed class FloatingAssetPanel : Panel
{
    private const double AssetSpacing = 24;
    private const double BlockSpacing = 6;
    private const double FallbackWidth = 900;
    private readonly int _rightFloatCount;

    public FloatingAssetPanel(int rightFloatCount)
    {
        _rightFloatCount = Math.Max(0, rightFloatCount);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        double panelWidth = double.IsInfinity(availableSize.Width) ? FallbackWidth : availableSize.Width;
        int rightFloatCount = Math.Min(_rightFloatCount, Children.Count);
        for (int i = 0; i < rightFloatCount; i++)
        {
            Children[i].Measure(new Size(panelWidth, double.PositiveInfinity));
        }

        IReadOnlyList<RightFloatLayout> rightFloats = GetRightFloatLayouts(panelWidth, rightFloatCount);
        double y = 0;

        for (int i = rightFloatCount; i < Children.Count; i++)
        {
            Control child = Children[i];
            double childWidth = GetFlowWidth(panelWidth, rightFloats, y);
            child.Measure(new Size(childWidth, double.PositiveInfinity));
            y += child.DesiredSize.Height;
            if (i < Children.Count - 1)
            {
                y += BlockSpacing;
            }
        }

        double rightFloatHeight = rightFloats.Count == 0 ? 0 : rightFloats.Max(rightFloat => rightFloat.Height);
        return new Size(panelWidth, Math.Max(rightFloatHeight, y));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        int rightFloatCount = Math.Min(_rightFloatCount, Children.Count);
        IReadOnlyList<RightFloatLayout> rightFloats = GetRightFloatLayouts(finalSize.Width, rightFloatCount);
        foreach (RightFloatLayout rightFloat in rightFloats)
        {
            rightFloat.Control.Arrange(new Rect(rightFloat.X, 0, rightFloat.Width, rightFloat.Height));
        }

        double y = 0;
        for (int i = rightFloatCount; i < Children.Count; i++)
        {
            Control child = Children[i];
            double childWidth = GetFlowWidth(finalSize.Width, rightFloats, y);
            child.Arrange(new Rect(0, y, childWidth, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            if (i < Children.Count - 1)
            {
                y += BlockSpacing;
            }
        }

        return finalSize;
    }

    private IReadOnlyList<RightFloatLayout> GetRightFloatLayouts(double panelWidth, int rightFloatCount)
    {
        List<RightFloatLayout> layouts = [];
        double rightEdge = panelWidth;
        for (int i = 0; i < rightFloatCount; i++)
        {
            Control control = Children[i];
            double width = Math.Min(control.DesiredSize.Width, panelWidth);
            double height = control.DesiredSize.Height;
            double x = Math.Max(0, rightEdge - width);
            layouts.Add(new RightFloatLayout(control, x, width, height));
            rightEdge = x - AssetSpacing;
        }

        return layouts;
    }

    private static double GetFlowWidth(double panelWidth, IReadOnlyList<RightFloatLayout> rightFloats, double y)
    {
        double activeFloatLeft = panelWidth;
        foreach (RightFloatLayout rightFloat in rightFloats)
        {
            if (y < rightFloat.Height)
            {
                activeFloatLeft = Math.Min(activeFloatLeft, rightFloat.X);
            }
        }

        return activeFloatLeft >= panelWidth
            ? panelWidth
            : Math.Max(0, activeFloatLeft - AssetSpacing);
    }

    private sealed record RightFloatLayout(Control Control, double X, double Width, double Height);
}
