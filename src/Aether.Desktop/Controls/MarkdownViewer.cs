using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using CommunityToolkit.Mvvm.DependencyInjection;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Desktop.Controls;

/// <summary>
/// Native Avalonia markdown renderer. Uses Markdig as parser; walks the AST
/// and emits Avalonia controls so rendering stays crisp at any DPI/scale.
/// Handles: headings, paragraphs, bold/italic, inline code, fenced code blocks,
/// ordered + unordered lists, blockquotes, thematic breaks, links.
/// </summary>
public sealed class MarkdownViewer : ContentControl, IDisposable
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(Markdown));

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<MarkdownViewer, bool>(nameof(IsError));

    /// <summary>
    /// r19 5.4: lets a fenced code block's Save button reach the owning
    /// ChatViewModel. MarkdownViewer stays a dumb, reusable renderer -
    /// (language, code, this viewer's full markdown text) is reported
    /// as-is; naming/writing policy lives entirely in the caller.
    /// </summary>
    public static readonly StyledProperty<Action<string?, string, string>?> RequestSaveCodeBlockProperty =
        AvaloniaProperty.Register<MarkdownViewer, Action<string?, string, string>?>(nameof(RequestSaveCodeBlock));

    public Action<string?, string, string>? RequestSaveCodeBlock
    {
        get => GetValue(RequestSaveCodeBlockProperty);
        set => SetValue(RequestSaveCodeBlockProperty, value);
    }

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly FontFamily MonoFamily =
        new("Cascadia Code,Fira Code,JetBrains Mono,Consolas,monospace");
    private readonly DispatcherTimer _renderTimer;
    private string _lastRenderedMarkdown = string.Empty;
    private bool _lastRenderedIsError;
    private double _lastRenderedFontSize;
    private int _renderVersion;

    // Incremental re-render (r8 03-performance.md 3.5): each top-level block's
    // exact source text is remembered alongside the control it produced, so a
    // streaming append only rebuilds the blocks whose source text actually
    // changed instead of the whole document every debounce tick.
    private readonly List<(string SourceText, Control Control)> _lastRenderedBlocks = [];
    private double _lastBlockRenderFontSize = double.NaN;
    internal int LastReusedBlockCount { get; private set; }
    internal int LastRebuiltBlockCount { get; private set; }

    // Cross-block drag selection: each markdown block (paragraph, code
    // fence, list item, table cell...) renders as its own independently
    // selectable control, so a drag can't natively span block boundaries.
    // Once a drag crosses from its starting block into another, every block
    // is selected as a whole and Ctrl+C copies the message's raw markdown
    // directly instead of relying on whichever single control has focus.
    private Control? _dragAnchorBlock;
    private bool _crossBlockSelectionActive;

    public MarkdownViewer()
    {
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _renderTimer.Tick += OnRenderTimerTick;

        AddHandler(PointerPressedEvent, OnViewerPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnViewerPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnViewerPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnViewerKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_crossBlockSelectionActive)
        {
            ClearAllBlockSelections();
            _crossBlockSelectionActive = false;
        }
        _dragAnchorBlock = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? HitTestBlock(e.GetPosition(this))
            : null;
    }

    private void OnViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragAnchorBlock is null || _crossBlockSelectionActive) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var current = HitTestBlock(e.GetPosition(this));
        if (current is not null && current != _dragAnchorBlock)
        {
            SelectAllBlocks();
            _crossBlockSelectionActive = true;
        }
    }

    private void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragAnchorBlock = null;
    }

    private void OnViewerKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_crossBlockSelectionActive || e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control)
            return;

        e.Handled = true;
        _ = CopyFullMarkdownAsync();
    }

    private async Task CopyFullMarkdownAsync()
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(Markdown ?? string.Empty);
        }
        catch
        {
            // Best-effort: no clipboard available on this platform/session.
        }
    }

    /// <summary>Top-level rendered block (direct child of the content StackPanel) under <paramref name="pointInViewer"/>, or null outside any block.</summary>
    private Control? HitTestBlock(Point pointInViewer)
    {
        if (Content is not StackPanel panel) return null;
        var pointInPanel = this.TranslatePoint(pointInViewer, panel) ?? pointInViewer;
        foreach (var child in panel.Children)
            if (child.Bounds.Contains(pointInPanel))
                return child;
        return null;
    }

    private void SelectAllBlocks()
    {
        if (Content is not Control root) return;
        foreach (var stb in root.GetSelfAndVisualDescendants().OfType<SelectableTextBlock>())
            stb.SelectAll();
        foreach (var editor in root.GetSelfAndVisualDescendants().OfType<TextEditor>())
            editor.SelectAll();
    }

    private void ClearAllBlockSelections()
    {
        if (Content is not Control root) return;
        foreach (var stb in root.GetSelfAndVisualDescendants().OfType<SelectableTextBlock>())
            stb.ClearSelection();
        foreach (var editor in root.GetSelfAndVisualDescendants().OfType<TextEditor>())
            editor.Select(0, 0);
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == IsErrorProperty
            || change.Property == FontSizeProperty)
        {
            if (change.Property == MarkdownProperty)
            {
                // Throttle, don't debounce: during streaming, content changes faster
                // than the render interval, so restarting the timer on every change
                // would keep pushing the deadline out and never actually fire until
                // the stream pauses. Leaving an already-running timer alone gives a
                // steady render cadence instead.
                if (!_renderTimer.IsEnabled)
                    _renderTimer.Start();
            }
            else
            {
                _renderTimer.Stop();
                Render();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Dispose();
    }

    public void Dispose()
    {
        _renderTimer.Stop();
        _renderTimer.Tick -= OnRenderTimerTick;
    }

    private async void OnRenderTimerTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        await RenderAsync();
    }

    private void Render()
    {
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        var md = Markdown ?? string.Empty;
        if (md == _lastRenderedMarkdown
            && IsError == _lastRenderedIsError
            && Math.Abs(FontSize - _lastRenderedFontSize) < 0.01)
        {
            return;
        }

        _lastRenderedMarkdown = md;
        _lastRenderedIsError = IsError;
        _lastRenderedFontSize = FontSize;
        var version = ++_renderVersion;

        if (string.IsNullOrEmpty(md))
        {
            Content = null;
            return;
        }

        if (IsError)
        {
            Content = new SelectableTextBlock
            {
                Text = md,
                TextWrapping = TextWrapping.Wrap,
                FontSize = FontSize,
                Foreground = new SolidColorBrush(Color.Parse("#EF5350"))
            };
            return;
        }

        var doc = await Task.Run(() => Markdig.Markdown.Parse(md, Pipeline));
        if (version != _renderVersion)
            return;

        try
        {
            Render(doc, md);
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex);
            Content = new SelectableTextBlock
            {
                Text = md,
                TextWrapping = TextWrapping.Wrap,
                FontSize = FontSize
            };
            _lastRenderedBlocks.Clear();
        }
    }

    // A markdown rendering bug must never be fatal to the whole app; this is
    // a last-resort net around any future RenderBlock defect, not a
    // substitute for fixing the specific defect it catches.
    private static void LogRenderFailure(Exception ex)
    {
        try
        {
            var log = Ioc.Default.GetService<IRuntimeLogService>();
            log?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service,
                $"MarkdownViewer render failed: {ex.GetType().Name}: {ex.Message}"));
        }
        catch
        {
            // Best-effort logging only; never let the fallback path itself throw.
        }
    }

    private void Render(MarkdownDocument doc, string sourceText)
    {
        var blockSources = doc.Select(b => SourceTextFor(b, sourceText)).ToList();

        // A font-size change re-renders every block at the new size; nothing
        // from a previous render at a different size may be reused.
        var fontSizeChanged = Math.Abs(FontSize - _lastBlockRenderFontSize) >= 0.01;
        _lastBlockRenderFontSize = FontSize;

        var previousSources = _lastRenderedBlocks.Select(b => b.SourceText).ToList();
        var reusePrefixLength = fontSizeChanged || Content is not StackPanel
            ? 0
            : ComputeReusePrefixLength(blockSources, previousSources);

        if (Content is not StackPanel panel)
        {
            panel = new StackPanel { Spacing = 4 };
            Content = panel;
            _lastRenderedBlocks.Clear();
        }

        while (panel.Children.Count > reusePrefixLength)
            panel.Children.RemoveAt(panel.Children.Count - 1);
        while (_lastRenderedBlocks.Count > reusePrefixLength)
            _lastRenderedBlocks.RemoveAt(_lastRenderedBlocks.Count - 1);

        for (var i = reusePrefixLength; i < doc.Count; i++)
        {
            var control = RenderBlock(doc[i]);
            panel.Children.Add(control);
            _lastRenderedBlocks.Add((blockSources[i], control));
        }

        LastReusedBlockCount = reusePrefixLength;
        LastRebuiltBlockCount = doc.Count - reusePrefixLength;
    }

    internal static string SourceTextFor(Block block, string sourceText)
    {
        var span = block.Span;
        if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > sourceText.Length)
            return string.Empty;
        return sourceText.Substring(span.Start, span.Length);
    }

    internal static IReadOnlyList<string> BlockSourceTexts(MarkdownDocument doc, string sourceText) =>
        doc.Select(b => SourceTextFor(b, sourceText)).ToList();

    /// <summary>
    /// Longest common prefix of unchanged block source text between two
    /// renders: this many leading blocks may keep their existing control,
    /// everything from the first mismatch onward must be rebuilt. An empty
    /// source text (an invalid/unavailable span) never counts as a match,
    /// so a bad span forces a safe rebuild instead of a false-positive reuse.
    /// </summary>
    internal static int ComputeReusePrefixLength(IReadOnlyList<string> currentBlockSources, IReadOnlyList<string> previousBlockSources)
    {
        var n = 0;
        while (n < currentBlockSources.Count
               && n < previousBlockSources.Count
               && currentBlockSources[n].Length > 0
               && currentBlockSources[n] == previousBlockSources[n])
        {
            n++;
        }
        return n;
    }

    // ── Block rendering ──────────────────────────────────────────────────────

    private Control RenderBlock(Block block) => block switch
    {
        HeadingBlock h    => RenderHeading(h),
        FencedCodeBlock c => RenderFencedCode(c),
        CodeBlock c       => RenderCodeBlock(c),
        Table t           => RenderTable(t),
        ListBlock l       => RenderList(l),
        QuoteBlock q      => RenderQuote(q),
        ThematicBreakBlock => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8),
            Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128))
        },
        ParagraphBlock p  => RenderParagraph(p),
        _                 => RenderFallback(block)
    };

    private SelectableTextBlock RenderHeading(HeadingBlock h)
    {
        double size = h.Level switch { 1 => 22, 2 => 18, 3 => 16, 4 => 15, _ => FontSize };
        var weight = h.Level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
        return new SelectableTextBlock
        {
            Inlines = BuildInlines(h.Inline),
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, h.Level == 1 ? 10 : 6, 0, 3)
        };
    }

    private SelectableTextBlock RenderParagraph(LeafBlock? p) => new()
    {
        Inlines = BuildInlines((p as ParagraphBlock)?.Inline),
        TextWrapping = TextWrapping.Wrap,
        FontSize = FontSize,
        LineHeight = FontSize * 1.65,
        Margin = new Thickness(0, 1)
    };

    /// <summary>
    /// A code fence cut off mid-stream (truncated at the token cap, or a
    /// fence opened right as the render timer ticks) can leave Markdig with
    /// a line group whose Lines array is null; joining that as text must
    /// never throw.
    /// </summary>
    internal static string JoinLines(LeafBlock? block)
    {
        if (block?.Lines.Lines is not { } lines)
            return string.Empty;
        return string.Join("\n", lines.Take(block.Lines.Count).Select(l => l.ToString()));
    }

    private Control RenderFallback(Block block)
    {
        var raw = block is LeafBlock lb ? JoinLines(lb) : string.Empty;
        return new SelectableTextBlock
        {
            Text = raw,
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSize
        };
    }

    private Border RenderFencedCode(FencedCodeBlock c) => CodeBorder(JoinLines(c), c.Info);

    private Border RenderCodeBlock(CodeBlock c) => CodeBorder(JoinLines(c), null);

    private Border CodeBorder(string code, string? lang)
    {
        var lineCount = code.Split('\n').Length;
        var normalizedLanguage = NormalizeFenceLanguage(lang);

        // r19 5.4: every code block gets a Save button, not just labeled
        // ones, so the header row is always built now (it used to be null
        // for an unlabeled fence).
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        if (!string.IsNullOrWhiteSpace(lang))
        {
            header.Children.Add(new TextBlock
            {
                Text = lang,
                FontSize = 11,
                Opacity = 0.5,
                FontFamily = MonoFamily,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var saveButton = new Button
        {
            Content = "Save",
            FontSize = 10,
            Padding = new Thickness(6, 1),
            Opacity = 0.55
        };
        // Bound imperatively (not via a XAML binding) since this control tree is
        // built entirely in code; RequestSaveCodeBlock is read live at click
        // time so it reflects whatever the host currently has wired up.
        saveButton.Click += (_, _) => RequestSaveCodeBlock?.Invoke(lang, code, Markdown ?? string.Empty);
        header.Children.Add(saveButton);

        var codeFontSize = FontSize - 1;
        var minHeight = Math.Max(28, (FontSize + 4) * Math.Max(1, lineCount));

        // AvaloniaEdit's TextEditor is used strictly read-only, and only when there's
        // a recognized language and enough lines to make syntax coloring worth the
        // heavier control; short or unrecognized-language blocks get a plain text run.
        Control codeBlock = lineCount > 20 && !string.IsNullOrWhiteSpace(normalizedLanguage)
            ? new TextEditor
            {
                Text = code,
                FontFamily = MonoFamily,
                FontSize = codeFontSize,
                IsReadOnly = true,
                ShowLineNumbers = false,
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(normalizedLanguage),
                Background = Brushes.Transparent,
                Foreground = Brushes.WhiteSmoke,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = minHeight,
                MaxHeight = 420
            }
            : new SelectableTextBlock
            {
                Text = code,
                FontFamily = MonoFamily,
                FontSize = codeFontSize,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = Brushes.WhiteSmoke,
                MinHeight = minHeight
            };

        Control child = new StackPanel { Spacing = 0, Children = { header, codeBlock } };

        return new Border
        {
            Classes = { "code-block" },
            Background = new SolidColorBrush(Color.FromArgb(60, 100, 100, 100)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 4),
            Child = child
        };
    }

    public static string? NormalizeFenceLanguage(string? lang)
    {
        var key = (lang ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim()
            .TrimStart('.')
            .ToLowerInvariant();
        return key switch
        {
            "cs" or "csharp" => "C#",
            "fs" or "fsharp" => "F#",
            "vb" or "visualbasic" => "VB",
            "py" or "python" => "Python",
            "js" or "javascript" => "JavaScript",
            "ts" or "typescript" => "JavaScript",
            "bash" or "sh" or "shell" or "zsh" => "Bash",
            "json" => "JavaScript",
            "xml" or "xaml" or "axaml" => "XML",
            "html" or "htm" => "HTML",
            "css" or "scss" => "CSS",
            "sql" => "SQL",
            "diff" or "patch" => "Patch",
            "md" or "markdown" => "MarkDown",
            "cpp" or "cxx" or "cc" or "hpp" or "h" => "C++",
            "c" => "C++",
            "java" => "Java",
            _ => null
        };
    }

    private Panel RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(8, 2, 0, 2) };
        var n = int.TryParse(list.OrderedStart, out var orderedStart) ? orderedStart : 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var bullet = list.IsOrdered ? $"{n++}." : "•";
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var bulletTb = new TextBlock
            {
                Text = bullet,
                FontSize = FontSize,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = list.IsOrdered ? 20 : 12
            };

            var inner = new StackPanel { Spacing = 2 };
            Grid.SetColumn(inner, 1);
            foreach (var b in item) inner.Children.Add(RenderBlock(b));

            row.Children.Add(bulletTb);
            row.Children.Add(inner);
            panel.Children.Add(row);
        }
        return panel;
    }

    private Border RenderQuote(QuoteBlock quote)
    {
        var inner = new StackPanel { Spacing = 4 };
        foreach (var b in quote) inner.Children.Add(RenderBlock(b));
        return new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 150, 150, 150)),
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 4),
            Child = inner
        };
    }

    private Control RenderTable(Table table)
    {
        var columnCount = table.ColumnDefinitions.Count > 0
            ? table.ColumnDefinitions.Count
            : table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        if (columnCount == 0)
            return RenderFallback(table);

        var grid = new Grid { RowSpacing = 2, ColumnSpacing = 16 };
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(c == columnCount - 1 ? GridLength.Star : GridLength.Auto));

        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var columnIndex = 0;
            foreach (var cellBlock in row)
            {
                if (cellBlock is TableCell cell)
                {
                    var cellControl = RenderTableCell(cell, row.IsHeader);
                    Grid.SetRow(cellControl, rowIndex);
                    Grid.SetColumn(cellControl, columnIndex);
                    grid.Children.Add(cellControl);
                }
                columnIndex++;
            }

            rowIndex++;
            if (row.IsHeader)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
                    Margin = new Thickness(0, 2)
                };
                Grid.SetRow(separator, rowIndex);
                Grid.SetColumnSpan(separator, columnCount);
                grid.Children.Add(separator);
                rowIndex++;
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 4),
            Content = grid
        };
    }

    private StackPanel RenderTableCell(TableCell cell, bool isHeader)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var block in cell)
            panel.Children.Add(RenderBlock(block));

        if (isHeader)
            foreach (var child in panel.Children.OfType<SelectableTextBlock>())
                child.FontWeight = FontWeight.Bold;

        return panel;
    }

    // ── Inline rendering ─────────────────────────────────────────────────────

    private InlineCollection BuildInlines(ContainerInline? container)
    {
        var col = new InlineCollection();
        if (container is null) return col;
        foreach (var inline in container)
            AddInline(col, inline);
        return col;
    }

    private void AddInline(InlineCollection col, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                col.Add(new Run { Text = lit.Content.ToString() });
                break;

            case CodeInline code:
                col.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = MonoFamily,
                    FontSize = FontSize - 1,
                    Background = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128))
                });
                break;

            case EmphasisInline em:
                var span = new Span();
                foreach (var child in em)
                    AddInline(span.Inlines, child);
                if (em.DelimiterCount >= 2)
                    span.FontWeight = FontWeight.Bold;
                else
                    span.FontStyle = FontStyle.Italic;
                col.Add(span);
                break;

            case LinkInline link:
                col.Add(BuildLinkInline(link));
                break;

            case HtmlInline:
                break; // skip raw HTML

            case LineBreakInline lb:
                col.Add(lb.IsHard ? new LineBreak() : new Run { Text = " " });
                break;

            case ContainerInline ci:
                foreach (var child in ci)
                    AddInline(col, child);
                break;
        }
    }

    /// <summary>
    /// Only http/https links are made clickable; everything else (file:,
    /// javascript:, data:, or an unparsable URL) renders as plain styled
    /// text so a malicious or malformed link can never be launched.
    /// </summary>
    public static bool IsSafeLinkScheme(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4FC3F7"));

    private Avalonia.Controls.Documents.Inline BuildLinkInline(LinkInline link)
    {
        var url = link.Url ?? string.Empty;
        if (!IsSafeLinkScheme(url))
            return BuildPlainLinkSpan(link);

        var textBlock = new TextBlock
        {
            Inlines = BuildInlines(link),
            FontSize = FontSize,
            Foreground = LinkBrush,
            TextDecorations = Avalonia.Media.TextDecorations.Underline
        };

        var button = new Button
        {
            Content = textBlock,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, url);
        button.Click += (_, _) => OpenUrl(url);

        return new InlineUIContainer(button);
    }

    private Span BuildPlainLinkSpan(LinkInline link)
    {
        var span = new Span { Foreground = LinkBrush };
        foreach (var child in link)
            AddInline(span.Inlines, child);
        return span;
    }

    private static void OpenUrl(string url)
    {
        if (!IsSafeLinkScheme(url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Best-effort: no OS handler registered, or the launch failed; nothing more to do from here.
        }
    }
}
