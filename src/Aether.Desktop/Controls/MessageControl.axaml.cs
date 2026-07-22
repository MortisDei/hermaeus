using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Aether.Desktop.Controls;

public partial class MessageControl : UserControl
{
    /// <summary>
    /// r19 6.1: the memory pill flyout's "Open in Memories" link needs
    /// ChatViewModel's command, but the pill sits inside a SECOND, nested
    /// ItemsControl (MemorySources) so the usual $parent[ItemsControl]
    /// ancestor-climb used elsewhere in this control would resolve to the
    /// wrong (inner) ItemsControl. The caller (ChatView's MessagesList
    /// DataTemplate, where only one ItemsControl ancestor is in scope) binds
    /// this property to the real command instead.
    /// </summary>
    public static readonly StyledProperty<ICommand?> OpenMemoryCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(OpenMemoryCommand));

    public ICommand? OpenMemoryCommand
    {
        get => GetValue(OpenMemoryCommandProperty);
        set => SetValue(OpenMemoryCommandProperty, value);
    }

    public MessageControl() => InitializeComponent();
}
