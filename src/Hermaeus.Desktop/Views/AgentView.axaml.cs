using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;

namespace Hermaeus.Desktop.Views;

public partial class AgentView : UserControl
{
    private Hermaeus.ViewModels.AgentViewModel? _wiredViewModel;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public AgentView()
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
        _callbackGeneration++;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;
        _wiredViewModel = null;
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not Hermaeus.ViewModels.AgentViewModel vm) return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;
        vm.RequestWorkspaceRootPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose workspace folder",
                AllowMultiple = false
            });

            if (folders.Count > 0 && OwnsCallbacks(generation))
                vm.WorkspaceRoot = folders[0].Path.LocalPath;
        };
        vm.RequestOpenFolder = path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows()
                        ? "explorer"
                        : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                    UseShellExecute = true
                };
                psi.ArgumentList.Add(path);
                _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to open '{path}': {ex.Message}");
            }
        };
        vm.RequestDeleteTaskConfirmation = async item =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Delete agent run",
                $"Permanently delete the historical run '{item.Goal}' and its persisted sub-tasks? This removes its transcript, trace, log, and report.");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };
        var ownedRequestWorkspaceRootPicker = vm.RequestWorkspaceRootPicker;
        var ownedRequestDeleteTaskConfirmation = vm.RequestDeleteTaskConfirmation;
        _ownsCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestWorkspaceRootPicker == ownedRequestWorkspaceRootPicker
            && vm.RequestDeleteTaskConfirmation == ownedRequestDeleteTaskConfirmation;
        _releaseCallbacks = () =>
        {
            if (vm.RequestWorkspaceRootPicker == ownedRequestWorkspaceRootPicker) vm.RequestWorkspaceRootPicker = null;
            if (vm.RequestDeleteTaskConfirmation == ownedRequestDeleteTaskConfirmation) vm.RequestDeleteTaskConfirmation = null;
        };
    }
}
