using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Aether.Core.Models;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class ServicesView : UserControl
{
    public static readonly IValueConverter StatusColor = new StatusColorConverter();

    public ServicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ServicesViewModel vm) return;
        foreach (var srv in vm.Servers)
            WireFilePickers(srv);

        vm.Servers.CollectionChanged += (_, _) =>
        {
            foreach (var srv in vm.Servers)
                WireFilePickers(srv);
        };
    }

    private void WireFilePickers(ServerProcessViewModel srv)
    {
        srv.RequestFilePicker = async propertyName =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var isExe   = propertyName == nameof(ServerProcessViewModel.ExecutablePath);
            var options = new FilePickerOpenOptions
            {
                Title         = isExe ? "Select llama-server executable" : "Select model (.gguf)",
                AllowMultiple = false,
                FileTypeFilter = isExe
                    ? [new FilePickerFileType("Executable") { Patterns = ["*"] }]
                    : [new FilePickerFileType("GGUF model") { Patterns = ["*.gguf", "*.bin"] },
                       new FilePickerFileType("All files")  { Patterns = ["*"] }]
            };

            var files = await top.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0) return;

            var path = files[0].Path.LocalPath;
            if (isExe) srv.ExecutablePath = path;
            else       srv.ModelPath      = path;
        };
    }
}

public sealed class StatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        (ServerStatus)(v ?? ServerStatus.Stopped) switch
        {
            ServerStatus.Running  => new SolidColorBrush(Color.Parse("#4CAF50")),
            ServerStatus.Starting => new SolidColorBrush(Color.Parse("#FF9800")),
            ServerStatus.Error    => new SolidColorBrush(Color.Parse("#F44336")),
            _                     => new SolidColorBrush(Color.Parse("#757575"))
        };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotImplementedException();
}
