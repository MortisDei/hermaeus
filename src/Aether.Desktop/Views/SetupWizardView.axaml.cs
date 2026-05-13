using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class SetupWizardView : UserControl
{
    public static readonly IValueConverter Step0 = new StepIndexConverter(0);
    public static readonly IValueConverter Step1 = new StepIndexConverter(1);
    public static readonly IValueConverter Step2 = new StepIndexConverter(2);
    public static readonly IValueConverter Step3 = new StepIndexConverter(3);
    public static readonly IValueConverter Step4 = new StepIndexConverter(4);
    public static readonly IValueConverter Step5 = new StepIndexConverter(5);
    public static readonly IValueConverter Last = new StepIndexConverter(5, isLast: true);
    public static readonly IValueConverter NotLast = new StepIndexConverter(5, invert: true);

    public SetupWizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SetupWizardViewModel vm) return;
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

        vm.RequestLocalAiAssetsRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose local AI assets folder",
                AllowMultiple = false
            });
            if (folders.Count > 0)
                vm.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.RequestModelFolderPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose GGUF model",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("GGUF model") { Patterns = ["*.gguf", "*.bin"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });
            if (files.Count > 0)
                vm.ModelFolder = files[0].Path.LocalPath;
        };
    }
}

public sealed class StepIndexConverter : IValueConverter
{
    private readonly int _target;
    private readonly bool _invert;
    private readonly bool _isLast;

    public StepIndexConverter(int target, bool invert = false, bool isLast = false)
    {
        _target = target;
        _invert = invert;
        _isLast = isLast;
    }

    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        if (v is not int index) return false;
        var matched = _isLast ? index >= _target : index == _target;
        return _invert ? !matched : matched;
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}
