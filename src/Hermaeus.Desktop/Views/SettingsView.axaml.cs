using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(PageScroller, e);

    private static void OnUiAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not UiSettingsViewModel ui) return;

        if (e.PropertyName is nameof(UiSettingsViewModel.HeadingFontFamily)
            or nameof(UiSettingsViewModel.BodyFontFamily)
            or nameof(UiSettingsViewModel.MonoFontFamily)
            or nameof(UiSettingsViewModel.FontSize))
        {
            AppFontService.Apply(ui.HeadingFontFamily, ui.BodyFontFamily, ui.MonoFontFamily, ui.FontSize);
        }

        if (e.PropertyName is nameof(UiSettingsViewModel.SelectedTheme))
        {
            AppThemeService.Apply(ui.SelectedTheme);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        // r21: live-preview font/size/theme changes as the user types,
        // mirroring what AppFontService/AppThemeService apply from persisted
        // settings at startup. -= before += keeps this idempotent if
        // DataContext is set again.
        vm.Ui.PropertyChanged -= OnUiAppearanceChanged;
        vm.Ui.PropertyChanged += OnUiAppearanceChanged;

        vm.Data.RequestDataRootPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose Hermaeus data folder");
            if (folders.Count > 0)
                vm.Data.DataRootDirectory = folders[0].Path.LocalPath;
        };

        vm.Data.RequestDataRootMigrationConfirmation = async plan =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var moveDescription = plan.FilesToMove > 0
                ? $"Move {plan.FilesToMove} existing Hermaeus workspace file(s)"
                : "Use the destination data folder";
            var dialog = new ConfirmActionDialog(
                "Confirm data folder change",
                $"Current data folder:\n{plan.PreviousDataRoot}\n\nDestination:\n{plan.CurrentDataRoot}\n\n{moveDescription}. Existing workspace state will be handled by Hermaeus' safe migration path.");
            return await dialog.ShowDialog<bool>(owner);
        };

        vm.Data.RequestLocalAiAssetsRootPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose local AI assets folder");
            if (folders.Count > 0)
                vm.Data.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.Data.RequestBackupDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose backup folder");
            if (folders.Count > 0)
                vm.Data.BackupDirectory = folders[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose Hermaeus backup zip",
                [
                    new FilePickerFileType("Hermaeus backup") { Patterns = ["*.zip"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                vm.Data.RestoreBackupPath = files[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupConfirmation = async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new RestoreBackupConfirmationDialog();
            return await dialog.ShowDialog<bool>(owner);
        };

        // Voice's own file pickers (Python/script/model/output/voice-sample) are wired
        // from ServicesView now that the "Voice providers" card lives there; Tts is a
        // shared singleton so either view wiring them is equally valid.

        vm.LocalAiSetup.RequestCopyToClipboard = async text =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
                await top.Clipboard.SetTextAsync(text);
        };
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null
            ? []
            : await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
    }

    private async Task<IReadOnlyList<IStorageFile>> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null
            ? []
            : await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters
            });
    }
}
