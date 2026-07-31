using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Hermaeus.Desktop.Views;

public partial class SettingsVoiceSectionView : UserControl
{
    public SettingsVoiceSectionView() => InitializeComponent();

    /// <summary>
    /// r29 doc 01 1.2: the chevron beside a channel's voice box. AutoCompleteBox
    /// has no built-in open affordance, so the drop-down is opened here. Kept in
    /// code-behind rather than as a view-model command because the target is an
    /// Avalonia control and Hermaeus.ViewModels must never reference Avalonia.
    /// </summary>
    private void OnShowChannelVoiceList(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Parent is not Visual row) return;
        if (row.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault() is not { } box) return;

        box.Focus();
        box.IsDropDownOpen = true;
    }
}
