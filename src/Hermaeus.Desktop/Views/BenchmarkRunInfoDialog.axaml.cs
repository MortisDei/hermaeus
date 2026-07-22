using Avalonia.Controls;
using Avalonia.Interactivity;
using Hermaeus.Core.Models;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class BenchmarkRunInfoDialog : Window
{
    private BenchmarkRunInfoViewModel? _dataContext;

    public BenchmarkRunInfoDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _dataContext = DataContext as BenchmarkRunInfoViewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_dataContext is null) return;
        await _dataContext.ExportRunAsync();
    }
}

public sealed class BenchmarkRunInfoViewModel
{
    private readonly BenchmarkRun _run;
    private readonly BenchmarkViewModel? _parentVm;

    public string Title => $"{_run.SuiteName} · {_run.ModelName}";
    public string Summary => $"{_run.RankingScore:P0} · pass {_run.PassRate:P0} · median {_run.MedianApproxTokensPerSecond:F1} tok/s";
    public string Started => _run.StartedAt.ToLocalTime().ToString("g");
    public string Status => _run.Status;
    public string Score => _run.RankingScore.ToString("P0");
    public string PassRate => _run.PassRate.ToString("P0");
    public string Speed => $"median {_run.MedianApproxTokensPerSecond:F1} tok/s";
    public List<BenchmarkResultViewModel> ResultSummaries { get; }

    public BenchmarkRunInfoViewModel(BenchmarkRun run, BenchmarkViewModel? parentVm = null)
    {
        _run = run;
        _parentVm = parentVm;
        ResultSummaries = run.Results.Select(r => new BenchmarkResultViewModel(r)).ToList();
    }

    public async Task ExportRunAsync()
    {
        if (_parentVm is null) return;
        await _parentVm.ExportRunAsync(new BenchmarkRunViewModel(_run));
    }
}
