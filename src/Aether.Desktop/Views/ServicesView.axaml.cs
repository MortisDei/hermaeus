using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Aether.Core.Models;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class ServicesView : UserControl
{
    public static readonly IValueConverter StatusColor = new StatusColorConverter();
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;
    private ServicesViewModel? _wiredViewModel;

    public ServicesView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(PageScroller, e);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wiredViewModel is not null && _collectionChangedHandler is not null)
            _wiredViewModel.Servers.CollectionChanged -= _collectionChangedHandler;

        if (DataContext is not ServicesViewModel vm) return;
        _wiredViewModel = vm;
        
        foreach (var srv in vm.Servers)
            WireFilePickers(srv);

        // Create and store handler to allow unsubscribing later
        _collectionChangedHandler = (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (ServerProcessViewModel srv in e.NewItems)
                    WireFilePickers(srv);
            }
        };
        
        vm.Servers.CollectionChanged += _collectionChangedHandler;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // Clean up event subscriptions when view is unloaded
        if (_wiredViewModel is not null && _collectionChangedHandler is not null)
            _wiredViewModel.Servers.CollectionChanged -= _collectionChangedHandler;

        _wiredViewModel = null;
        _collectionChangedHandler = null;
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

            if (!isExe && !string.IsNullOrWhiteSpace(srv.SuggestedModelBrowseDirectory))
            {
                var startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(srv.SuggestedModelBrowseDirectory);
                if (startFolder is not null)
                    options.SuggestedStartLocation = startFolder;
            }

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
        Avalonia.AvaloniaProperty.UnsetValue;
}
