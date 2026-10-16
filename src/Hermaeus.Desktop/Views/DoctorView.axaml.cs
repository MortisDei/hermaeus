using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Hermaeus.Core.Models;
using Hermaeus.ViewModels;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class DoctorView : UserControl
{
    private DoctorViewModel? _wiredViewModel;
    private Func<string, Task<bool>>? _copy;
    private Func<string, string, Task<bool>>? _confirmation;

    public DoctorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        if (_wiredViewModel is { } vm)
        {
            if (vm.RequestCopyToClipboard == _copy) vm.RequestCopyToClipboard = null;
            if (vm.RequestConfirmAsync == _confirmation) vm.RequestConfirmAsync = null;
        }
        _wiredViewModel = null;
        _copy = null;
        _confirmation = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not DoctorViewModel vm) return;
        _wiredViewModel = vm;
        Func<string, Task<bool>>? copy = null;
        Func<string, string, Task<bool>>? confirmation = null;
        bool OwnsCopy() => ReferenceEquals(_wiredViewModel, vm) && vm.RequestCopyToClipboard == copy;
        bool OwnsConfirmation() => ReferenceEquals(_wiredViewModel, vm) && vm.RequestConfirmAsync == confirmation;
        copy = async text =>
        {
            if (!OwnsCopy()) return false;
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return false;
            try { await top.Clipboard.SetTextAsync(text); return OwnsCopy(); }
            catch { return false; }
        };
        confirmation = async (title, message) =>
        {
            if (!OwnsConfirmation() || TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog(title, message);
            return await dialog.ShowDialog<bool>(owner) && OwnsConfirmation();
        };
        _copy = copy;
        _confirmation = confirmation;
        vm.RequestCopyToClipboard = copy;
        vm.RequestConfirmAsync = confirmation;
    }
}

public sealed class DoctorStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        return v switch
        {
            DoctorCheckStatus.Ready => StatusPalette.Ok,
            DoctorCheckStatus.Warning => StatusPalette.Warn,
            DoctorCheckStatus.Error => StatusPalette.Error,
            _ => StatusPalette.Neutral
        };
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}
