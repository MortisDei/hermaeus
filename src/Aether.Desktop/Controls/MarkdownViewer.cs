using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

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

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly FontFamily MonoFamily =
        new("Cascadia Code,Fira Code,JetBrains Mono,Consolas,monospace");
    private readonly DispatcherTimer _renderTimer;
    private string _lastRenderedMarkdown = string.Empty;
    private bool _lastRenderedIsError;
    private double _lastRenderedFontSize;

    public MarkdownViewer()
    {
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _renderTimer.Tick += OnRenderTimerTick;
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
                _renderTimer.Stop();
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

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        Render();
    }

    private void Render()
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

        var doc = Markdig.Markdown.Parse(md, Pipeline);
        var panel = new StackPanel { Spacing = 4 };
        foreach (var block in doc)
            panel.Children.Add(RenderBlock(block));

        Content = panel;
    }

    // ── Block rendering ──────────────────────────────────────────────────────

    private Control RenderBlock(Block block) => block switch
    {
        HeadingBlock h    => RenderHeading(h),
        FencedCodeBlock c => RenderFencedCode(c),
        CodeBlock c       => RenderCodeBlock(c),
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

    private Control RenderFallback(Block block)
    {
        var raw = block is LeafBlock lb
            ? string.Join("\n", lb.Lines.Lines.Take(lb.Lines.Count).Select(l => l.ToString()))
            : string.Empty;
        return new SelectableTextBlock
        {
            Text = raw,
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSize
        };
    }

    private Border RenderFencedCode(FencedCodeBlock c)
    {
        var code = string.Join("\n",
            c.Lines.Lines.Take(c.Lines.Count).Select(l => l.ToString()));
        return CodeBorder(code, c.Info);
    }

    private Border RenderCodeBlock(CodeBlock c)
    {
        var code = string.Join("\n",
            c.Lines.Lines.Take(c.Lines.Count).Select(l => l.ToString()));
        return CodeBorder(code, null);
    }

    private Border CodeBorder(string code, string? lang)
    {
        var header = !string.IsNullOrWhiteSpace(lang)
            ? new TextBlock
            {
                Text = lang,
                FontSize = 11,
                Opacity = 0.5,
                Margin = new Thickness(0, 0, 0, 6),
                FontFamily = MonoFamily
            }
            : null;

        var codeBlock = new TextEditor
        {
            Text = code,
            FontFamily = MonoFamily,
            FontSize = FontSize - 1,
            IsReadOnly = true,
            ShowLineNumbers = false,
            SyntaxHighlighting = ResolveHighlighting(lang),
            Background = Brushes.Transparent,
            Foreground = Brushes.WhiteSmoke,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = Math.Max(28, (FontSize + 4) * Math.Max(1, code.Split('\n').Length)),
            MaxHeight = 420
        };

        Control child = header is not null
            ? new StackPanel { Spacing = 0, Children = { header, codeBlock } }
            : codeBlock;

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

    private static IHighlightingDefinition? ResolveHighlighting(string? lang)
    {
        var normalized = NormalizeFenceLanguage(lang);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        return HighlightingManager.Instance.GetDefinition(normalized);
    }

    private Panel RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(8, 2, 0, 2) };
        int n = list.OrderedStart is not null ? int.Parse(list.OrderedStart) : 1;

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
                var linkSpan = new Span
                {
                    Foreground = new SolidColorBrush(Color.Parse("#4FC3F7"))
                };
                foreach (var child in link)
                    AddInline(linkSpan.Inlines, child);
                col.Add(linkSpan);
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
}
