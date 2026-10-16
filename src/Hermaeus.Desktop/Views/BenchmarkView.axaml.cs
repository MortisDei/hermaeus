using Avalonia.Controls;
using Avalonia.Interactivity;
using Hermaeus.Desktop.Views;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class BenchmarkView : UserControl
{
    private BenchmarkViewModel? _wiredViewModel;
    private BenchmarkViewModel? _loadedViewModel;
    private BenchmarkViewModel? _loadingViewModel;
    private Task? _loadingTask;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public BenchmarkView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => UnwireViewModel();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        WireViewModel();
        await EnsureLoadedAsync();
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
        await EnsureLoadedAsync();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_wiredViewModel is not { } vm || ReferenceEquals(_loadedViewModel, vm)) return;
        if (ReferenceEquals(_loadingViewModel, vm) && _loadingTask is { IsCompleted: false })
        {
            await _loadingTask;
            return;
        }
        _loadingViewModel = vm;
        _loadingTask = LoadContextAsync(vm, _callbackGeneration);
        await _loadingTask;
    }

    private async Task LoadContextAsync(BenchmarkViewModel vm, long generation)
    {
        try
        {
            if (!vm.HasLoaded)
                await vm.LoadAsync();
            if (!OwnsCallbacks(generation)) return;
            await vm.LoadInsightsCommand.ExecuteAsync(null);
            if (OwnsCallbacks(generation)) _loadedViewModel = vm;
        }
        catch (Exception ex)
        {
            // Native event handlers cannot return a failed Task to a command
            // caller. Keep failure visible and leave the view eligible to retry.
            if (OwnsCallbacks(generation)) vm.Status = $"Could not load benchmarks: {ex.Message}";
        }
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
        if (DataContext is not BenchmarkViewModel vm) return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;

        vm.RequestClearRunHistoryConfirmation = async () =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ClearBenchmarkHistoryDialog();
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.RequestShowCaseInfo = async (result) =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return;

            var infoVm = new BenchmarkCaseInfoViewModel(result.Result);
            var dialog = new BenchmarkCaseInfoDialog { DataContext = infoVm };
            await dialog.ShowDialog(owner);
        };
        vm.RequestShowRunInfo = async (runVm) =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner || runVm is null)
                return;

            var infoVm = new BenchmarkRunInfoViewModel(runVm.Run, vm);
            var dialog = new BenchmarkRunInfoDialog { DataContext = infoVm };
            await dialog.ShowDialog(owner);
        };
        var clear = vm.RequestClearRunHistoryConfirmation;
        var caseInfo = vm.RequestShowCaseInfo;
        var runInfo = vm.RequestShowRunInfo;
        _ownsCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestClearRunHistoryConfirmation == clear
            && vm.RequestShowCaseInfo == caseInfo && vm.RequestShowRunInfo == runInfo;
        _releaseCallbacks = () =>
        {
            if (vm.RequestClearRunHistoryConfirmation == clear) vm.RequestClearRunHistoryConfirmation = null;
            if (vm.RequestShowCaseInfo == caseInfo) vm.RequestShowCaseInfo = null;
            if (vm.RequestShowRunInfo == runInfo) vm.RequestShowRunInfo = null;
        };
    }
}
