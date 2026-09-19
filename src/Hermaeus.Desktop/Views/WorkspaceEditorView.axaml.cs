using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// The local workspace editor. AvaloniaEdit is the only editing surface so
/// the owner gets the same document, focus and keyboard behaviour on every
/// supported desktop platform. This control owns no filesystem authority:
/// saving is delegated through <see cref="SaveCommand"/>.
/// </summary>
public partial class WorkspaceEditorView : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<WorkspaceEditorView, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string> FilePathProperty =
        AvaloniaProperty.Register<WorkspaceEditorView, string>(nameof(FilePath), string.Empty);
    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<WorkspaceEditorView, ICommand?>(nameof(SaveCommand));

    private readonly TextEditor _editor;
    private bool _suppressTextChanged;
    private bool _attachedToVisualTree;
    private string _statusText = "AvaloniaEdit editor is ready.";

    public WorkspaceEditorView()
    {
        InitializeComponent();
        _editor = BuildEditor();
        _editor.TextChanged += OnEditorTextChanged;
        _editor.KeyDown += OnEditorKeyDown;
        EditorHost.Children.Add(_editor);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnEditorSizeChanged;
        EditorHost.SizeChanged += OnEditorHostSizeChanged;
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

    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var value = change.GetNewValue<string>() ?? string.Empty;
            if (!_suppressTextChanged && _editor.Text != value)
            {
                _suppressTextChanged = true;
                _editor.Text = value;
                _suppressTextChanged = false;
            }
        }
        else if (change.Property == FilePathProperty)
        {
            UpdateHighlighting();
        }
    }

    private static TextEditor BuildEditor() => new()
    {
        ShowLineNumbers = true,
        FontSize = 12,
        Background = Brushes.Transparent,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Padding = new Thickness(8),
        MinHeight = 260,
        IsReadOnly = false
    };

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged)
            return;

        _suppressTextChanged = true;
        SetCurrentValue(TextProperty, _editor.Text ?? string.Empty);
        _suppressTextChanged = false;
        EditorTextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || (e.KeyModifiers & KeyModifiers.Control) == 0)
            return;

        if (SaveCommand?.CanExecute(null) == true)
            SaveCommand.Execute(null);
        e.Handled = true;
    }

    private void UpdateHighlighting()
    {
        _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(
            Path.GetExtension(FilePath ?? string.Empty));
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attachedToVisualTree = true;
        _statusText = "AvaloniaEdit editor is ready.";
        UpdateEditorDiagnostics();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attachedToVisualTree = false;
        _statusText = "AvaloniaEdit editor is detached.";
        UpdateEditorDiagnostics();
    }

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateEditorDiagnostics();

    private void OnEditorHostSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateEditorDiagnostics();

    private void UpdateEditorDiagnostics()
    {
        if (EditorStatusText is null || EditorHost is null)
            return;

        EditorStatusText.Text = _statusText;
        var diagnostics =
            $"stage={_statusText}; attached={_attachedToVisualTree}; "
            + $"host={EditorHost.Bounds.Width:0}x{EditorHost.Bounds.Height:0}; "
            + $"editor={_editor.Bounds.Width:0}x{_editor.Bounds.Height:0}; "
            + $"editable={!_editor.IsReadOnly}; documentChars={Text.Length}; file={FilePath}";
        ToolTip.SetTip(EditorStatusText, diagnostics);
    }
}
