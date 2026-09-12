using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Hermaeus.Core.Models;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// Bounded local workspace editor. Monaco is preferred when the native
/// WebView adapter can host the local bundle; AvaloniaEdit remains the
/// functional fallback when the platform browser runtime is unavailable.
/// The bridge exposes only document text, a relative display path, theme, and
/// layout. It does not expose filesystem, navigation, or arbitrary script APIs.
/// </summary>
public partial class WorkspaceEditorView : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<WorkspaceEditorView, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string> FilePathProperty =
        AvaloniaProperty.Register<WorkspaceEditorView, string>(nameof(FilePath), string.Empty);

    private const int MaxMonacoCharacters = 2 * 1024 * 1024;
    private static readonly TimeSpan MonacoReadyTimeout = TimeSpan.FromSeconds(3);
    private TextEditor _fallbackEditor = null!;
    private NativeWebView? _webView;
    private CancellationTokenSource? _monacoReadyCts;
    private bool _monacoReady;
    private bool _monacoAttempted;
    private bool _suppressTextChanged;
    private bool _disposed;

    public WorkspaceEditorView()
    {
        InitializeComponent();
        BuildFallbackEditor();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    public event EventHandler? EditorTextChanged;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var value = change.GetNewValue<string>() ?? string.Empty;
            if (!_suppressTextChanged && _fallbackEditor is not null && _fallbackEditor.Text != value)
            {
                _suppressTextChanged = true;
                _fallbackEditor.Text = value;
                _suppressTextChanged = false;
            }

            if (_monacoReady)
                _ = SendDocumentToMonacoAsync();
        }
        else if (change.Property == FilePathProperty)
        {
            UpdateFallbackHighlighting();
            if (_monacoReady)
                _ = SendDocumentToMonacoAsync();
        }
    }

    private void BuildFallbackEditor()
    {
        _fallbackEditor = new TextEditor
        {
            ShowLineNumbers = true,
            FontSize = 12,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            MinHeight = 260
        };
        _fallbackEditor.TextChanged += OnFallbackTextChanged;
        EditorHost.Children.Add(_fallbackEditor);
        UpdateFallbackHighlighting();
    }

    private void OnFallbackTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged)
            return;
        _suppressTextChanged = true;
        SetCurrentValue(TextProperty, _fallbackEditor.Text ?? string.Empty);
        _suppressTextChanged = false;
        EditorTextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFallbackHighlighting()
    {
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(
            Path.GetExtension(FilePath ?? string.Empty));
        _fallbackEditor.SyntaxHighlighting = definition;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _disposed = false;
        if (!_monacoAttempted)
            TryCreateMonaco();
    }

    private void TryCreateMonaco()
    {
        _monacoAttempted = true;
        var indexPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco", "index.html");
        if (!File.Exists(indexPath))
        {
            SetFallback("AvaloniaEdit fallback: Monaco bundle is unavailable.");
            return;
        }

        try
        {
            var webView = new NativeWebView
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                MinHeight = 260
            };
            webView.AdapterCreated += OnAdapterCreated;
            webView.AdapterDestroyed += OnAdapterDestroyed;
            webView.NavigationCompleted += OnNavigationCompleted;
            webView.WebMessageReceived += OnWebMessageReceived;
            _webView = webView;
            EditorHost.Children.Clear();
            EditorHost.Children.Add(webView);
            EditorStatusText.Text = "Loading local Monaco...";
            webView.Navigate(new Uri(Path.GetFullPath(indexPath)));
            _monacoReadyCts?.Cancel();
            _monacoReadyCts?.Dispose();
            _monacoReadyCts = new CancellationTokenSource();
            _ = FallbackIfMonacoDoesNotBecomeReadyAsync(webView, _monacoReadyCts.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or TypeInitializationException or IOException or NotSupportedException or ArgumentException or UriFormatException)
        {
            SetFallback($"AvaloniaEdit fallback: native WebView unavailable ({ex.GetType().Name}).");
        }
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        if (!_disposed && ReferenceEquals(sender, _webView))
            EditorStatusText.Text = "Loading local Monaco...";
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        if (ReferenceEquals(sender, _webView) && !_disposed)
            SetFallback("AvaloniaEdit fallback: native WebView stopped.");
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (sender is not NativeWebView webView || !ReferenceEquals(webView, _webView))
            return;
        _ = InitializeMonacoAfterNavigationAsync(webView);
    }

    private async void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        try
        {
            if (_disposed || sender is not NativeWebView webView || !ReferenceEquals(webView, _webView))
                return;

            if (string.IsNullOrWhiteSpace(e.Body))
            {
                SetFallback("AvaloniaEdit fallback: empty editor bridge message.");
                return;
            }

            using var document = JsonDocument.Parse(e.Body);
            var type = document.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "ready")
            {
                _monacoReady = true;
                _monacoReadyCts?.Cancel();
                EditorStatusText.Text = "Monaco (local bundle)";
                await SendDocumentToMonacoAsync(webView);
                return;
            }

            if (type == "error")
            {
                SetFallback("AvaloniaEdit fallback: Monaco could not initialize.");
                return;
            }

            if (type == "changed" && _monacoReady)
            {
                var value = await ReadMonacoTextAsync(webView);
                if (_disposed || !ReferenceEquals(webView, _webView) || !_monacoReady)
                    return;
                if (value is null || value.Length > MaxMonacoCharacters)
                {
                    SetFallback("AvaloniaEdit fallback: file is too large for Monaco.");
                    return;
                }

                _suppressTextChanged = true;
                SetCurrentValue(TextProperty, value);
                _suppressTextChanged = false;
                EditorTextChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (JsonException)
        {
            SetFallback("AvaloniaEdit fallback: invalid editor bridge message.");
        }
        catch (Exception) when (!_disposed)
        {
            SetFallback("AvaloniaEdit fallback: editor bridge stopped.");
        }
    }

    private async Task InitializeMonacoAfterNavigationAsync(NativeWebView webView)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            if (!_disposed && ReferenceEquals(webView, _webView))
                await webView.InvokeScript("window.hermaeusEditorApi && window.hermaeusEditorApi.layout()");
        }
        catch (Exception) when (!_disposed)
        {
            SetFallback("AvaloniaEdit fallback: local Monaco navigation failed.");
        }
    }

    private async Task FallbackIfMonacoDoesNotBecomeReadyAsync(NativeWebView webView, CancellationToken ct)
    {
        try
        {
            await Task.Delay(MonacoReadyTimeout, ct);
            if (ct.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !_monacoReady && ReferenceEquals(webView, _webView))
                    SetFallback("AvaloniaEdit fallback: local Monaco did not become ready within 3 seconds.");
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_disposed)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !_monacoReady && ReferenceEquals(webView, _webView))
                    SetFallback("AvaloniaEdit fallback: local Monaco readiness check failed.");
            });
        }
    }

    private async Task SendDocumentToMonacoAsync(NativeWebView? expectedWebView = null)
    {
        var webView = _webView;
        if (!_monacoReady || _disposed || webView is null ||
            (expectedWebView is not null && !ReferenceEquals(expectedWebView, webView)))
            return;
        if (Text.Length > MaxMonacoCharacters)
        {
            SetFallback("AvaloniaEdit fallback: file is too large for Monaco.");
            return;
        }

        var path = JsonSerializer.Serialize(FilePath ?? string.Empty);
        var text = JsonSerializer.Serialize(Text ?? string.Empty);
        try
        {
            await webView.InvokeScript($"window.hermaeusEditorApi.setDocument({path}, {text})");
            if (!_disposed && ReferenceEquals(webView, _webView))
                await webView.InvokeScript($"window.hermaeusEditorApi.setTheme({JsonSerializer.Serialize(IsLightTheme() ? "light" : "dark")})");
        }
        catch (Exception) when (!_disposed)
        {
            SetFallback("AvaloniaEdit fallback: local Monaco bridge failed.");
        }
    }

    private static async Task<string?> ReadMonacoTextAsync(NativeWebView webView)
    {
        var result = await webView.InvokeScript("JSON.stringify(window.hermaeusEditorApi.getText())");
        return string.IsNullOrWhiteSpace(result) ? null : JsonSerializer.Deserialize<string>(result);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_monacoReady && _webView is not null)
            _ = SendThemeToMonacoAsync();
    }

    private async Task SendThemeToMonacoAsync()
    {
        var webView = _webView;
        try
        {
            if (!_disposed && _monacoReady && webView is not null && ReferenceEquals(webView, _webView))
                await webView.InvokeScript($"window.hermaeusEditorApi.setTheme({JsonSerializer.Serialize(IsLightTheme() ? "light" : "dark")})");
        }
        catch (Exception) when (!_disposed)
        {
            SetFallback("AvaloniaEdit fallback: theme update failed.");
        }
    }

    private bool IsLightTheme() => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;

    private void SetFallback(string status)
    {
        _monacoReadyCts?.Cancel();
        _monacoReadyCts?.Dispose();
        _monacoReadyCts = null;
        _monacoReady = false;
        var webView = _webView;
        _webView = null;
        if (webView is not null)
        {
            webView.AdapterCreated -= OnAdapterCreated;
            webView.AdapterDestroyed -= OnAdapterDestroyed;
            webView.NavigationCompleted -= OnNavigationCompleted;
            webView.WebMessageReceived -= OnWebMessageReceived;
            try { webView.Stop(); } catch { }
        }

        EditorHost.Children.Clear();
        EditorHost.Children.Add(_fallbackEditor);
        EditorStatusText.Text = status;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _disposed = true;
        _monacoReady = false;
        _monacoAttempted = false;
        _monacoReadyCts?.Cancel();
        _monacoReadyCts?.Dispose();
        _monacoReadyCts = null;

        var webView = _webView;
        _webView = null;
        if (webView is not null)
        {
            webView.AdapterCreated -= OnAdapterCreated;
            webView.AdapterDestroyed -= OnAdapterDestroyed;
            webView.NavigationCompleted -= OnNavigationCompleted;
            webView.WebMessageReceived -= OnWebMessageReceived;
            _ = DisposeWebViewAsync(webView);
        }

        EditorHost.Children.Clear();
        EditorHost.Children.Add(_fallbackEditor);
        EditorStatusText.Text = "AvaloniaEdit fallback: editor closed.";
    }

    private static async Task DisposeWebViewAsync(NativeWebView webView)
    {
        try
        {
            await webView.InvokeScript("window.hermaeusEditorApi && window.hermaeusEditorApi.dispose()");
        }
        catch { }

        try
        {
            webView.Stop();
        }
        catch { }
    }
}
