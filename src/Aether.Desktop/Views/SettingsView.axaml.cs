using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Aether.ViewModels;

namespace Aether.Desktop.Views;
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var max = Math.Max(0, PageScroller.Extent.Height - PageScroller.Viewport.Height);
        if (max <= 0) return;

        var next = Math.Clamp(PageScroller.Offset.Y - e.Delta.Y * 56, 0, max);
        if (Math.Abs(next - PageScroller.Offset.Y) < 0.1) return;

        PageScroller.Offset = new Vector(PageScroller.Offset.X, next);
        e.Handled = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        vm.RequestDataRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Aether data folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.DataRootDirectory = folders[0].Path.LocalPath;
        };
    }
}
