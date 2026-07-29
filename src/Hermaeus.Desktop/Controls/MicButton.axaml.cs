using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Controls;

/// <summary>r24 doc 05 5.4: shared dictation control. The host wires <see cref="ViewModel"/>
/// to a per-usage-site <see cref="MicButtonViewModel"/> and subscribes to its
/// TranscriptReady event to insert the result at its own text box's cursor - this
/// control does not own or know about that text box.</summary>
public partial class MicButton : UserControl
{
    public static readonly StyledProperty<MicButtonViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<MicButton, MicButtonViewModel?>(nameof(ViewModel));

    public MicButtonViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MicButton() => InitializeComponent();

    public static readonly IValueConverter IsUsableConverter = new FuncValueConverter<MicButtonState, bool>(
        state => state is MicButtonState.Ready or MicButtonState.Recording);

    public static readonly IValueConverter IsRecordingConverter = new FuncValueConverter<MicButtonState, bool>(
        state => state == MicButtonState.Recording);
}
