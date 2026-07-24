using Avalonia;
using Avalonia.Controls;

namespace Hermaeus.Desktop.Controls;

public partial class MossEmptyState : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<MossEmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<MossEmptyState, string?>(nameof(Hint));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<MossEmptyState, double>(nameof(IconSize), 40);

    public static readonly StyledProperty<string?> MossTipProperty =
        AvaloniaProperty.Register<MossEmptyState, string?>(nameof(MossTip));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string? MossTip
    {
        get => GetValue(MossTipProperty);
        set => SetValue(MossTipProperty, value);
    }

    public MossEmptyState() => InitializeComponent();
}
