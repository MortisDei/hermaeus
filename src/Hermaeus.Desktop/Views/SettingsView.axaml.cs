using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _wiredViewModel;
    private PropertyChangedEventHandler? _appearanceHandler;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public SettingsView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
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
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        if (_wiredViewModel is { } vm && _appearanceHandler is not null)
            vm.Ui.PropertyChanged -= _appearanceHandler;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;
        _appearanceHandler = null;
        _wiredViewModel = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded) return;
        if (ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not SettingsViewModel vm) return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;

        // Keep each visual's subscription distinct so an obsolete view cannot
        // remove the replacement's preview handler from the shared settings VM.
        _appearanceHandler = (sender, args) =>
        {
            if (OwnsCallbacks(generation))
                OnUiAppearanceChanged(sender, args);
        };
        vm.Ui.PropertyChanged += _appearanceHandler;

        vm.Data.RequestDataRootPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose Hermaeus data folder");
            if (folders.Count > 0 && OwnsCallbacks(generation))
            {
                vm.Data.DataRootDirectory = folders[0].Path.LocalPath;
                await vm.Data.ConfirmDataRootMigrationCommand.ExecuteAsync(null);
            }
        };

        vm.Data.RequestDataRootMigrationConfirmation = async plan =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var moveDescription = plan.FilesToMove > 0
                ? $"Move {plan.FilesToMove} existing Hermaeus workspace file(s)"
                : "Use the destination data folder";
            var dialog = new ConfirmActionDialog(
                "Confirm data folder change",
                $"Current data folder:\n{plan.PreviousDataRoot}\n\nDestination:\n{plan.CurrentDataRoot}\n\n{moveDescription}. Existing workspace state will be handled by Hermaeus' safe migration path.");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.Data.RequestDataRootMigrationRestartDecision = async () =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Data migration scheduled",
                "Hermaeus must restart before the data folder can be moved safely.",
                "Restart later",
                "Restart now");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.Data.RequestLocalAiAssetsRootPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose local AI assets folder");
            if (folders.Count > 0 && OwnsCallbacks(generation))
                vm.Data.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.Data.RequestBackupDirectoryPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose backup folder");
            if (folders.Count > 0 && OwnsCallbacks(generation))
                vm.Data.BackupDirectory = folders[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var files = await PickFileAsync(
                "Choose Hermaeus backup zip",
                [
                    new FilePickerFileType("Hermaeus backup") { Patterns = ["*.zip"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0 && OwnsCallbacks(generation))
                vm.Data.RestoreBackupPath = files[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupConfirmation = async () =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new RestoreBackupConfirmationDialog();
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.Data.RequestArtworkCacheClearConfirmation = async () =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Clear artwork cache",
                "Remove cached Hugging Face artwork only? Downloaded models and model manifests will not be changed.");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.Ui.RequestPetManifestPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return null;
            var files = await PickFileAsync(
                "Choose ChatGPT Pet manifest",
                [
                    new FilePickerFileType("ChatGPT Pet v2 manifest") { Patterns = ["pet.json"] },
                    new FilePickerFileType("JSON files") { Patterns = ["*.json"] }
                ]);
            return files.Count > 0 && OwnsCallbacks(generation) ? files[0].Path.LocalPath : null;
        };

        // Voice's own file pickers (Python/script/model/output/voice-sample) are wired
        // from ServicesView now that the "Voice providers" card lives there; Tts is a
        // shared singleton so either view wiring them is equally valid.

        vm.LocalAiSetup.RequestCopyToClipboard = async text =>
        {
            if (!OwnsCallbacks(generation)) return false;
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not { } clipboard)
                return false;
            try { await clipboard.SetTextAsync(text); return true; }
            catch { return false; }
        };
        CaptureCallbacks(vm);
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void CaptureCallbacks(SettingsViewModel vm)
    {
        var data = vm.Data;
        var root = data.RequestDataRootPicker;
        var migration = data.RequestDataRootMigrationConfirmation;
        var restart = data.RequestDataRootMigrationRestartDecision;
        var assets = data.RequestLocalAiAssetsRootPicker;
        var backup = data.RequestBackupDirectoryPicker;
        var restore = data.RequestRestoreBackupPicker;
        var confirmRestore = data.RequestRestoreBackupConfirmation;
        var artwork = data.RequestArtworkCacheClearConfirmation;
        var pet = vm.Ui.RequestPetManifestPicker;
        var clipboard = vm.LocalAiSetup.RequestCopyToClipboard;
        _ownsCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && data.RequestDataRootPicker == root;
        _releaseCallbacks = () =>
        {
            if (data.RequestDataRootPicker == root) data.RequestDataRootPicker = null;
            if (data.RequestDataRootMigrationConfirmation == migration) data.RequestDataRootMigrationConfirmation = null;
            if (data.RequestDataRootMigrationRestartDecision == restart) data.RequestDataRootMigrationRestartDecision = null;
            if (data.RequestLocalAiAssetsRootPicker == assets) data.RequestLocalAiAssetsRootPicker = null;
            if (data.RequestBackupDirectoryPicker == backup) data.RequestBackupDirectoryPicker = null;
            if (data.RequestRestoreBackupPicker == restore) data.RequestRestoreBackupPicker = null;
            if (data.RequestRestoreBackupConfirmation == confirmRestore) data.RequestRestoreBackupConfirmation = null;
            if (data.RequestArtworkCacheClearConfirmation == artwork) data.RequestArtworkCacheClearConfirmation = null;
            if (vm.Ui.RequestPetManifestPicker == pet) vm.Ui.RequestPetManifestPicker = null;
            if (vm.LocalAiSetup.RequestCopyToClipboard == clipboard) vm.LocalAiSetup.RequestCopyToClipboard = null;
        };
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null || _ownsCallbacks?.Invoke() != true
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
        return top is null || _ownsCallbacks?.Invoke() != true
            ? []
            : await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters
            });
    }
}
