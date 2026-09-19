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
        ModalWindowPlacement.ScheduleCenterOnOwner(this);
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
    public string EvaluatorVersion => string.IsNullOrWhiteSpace(_run.EvaluatorVersion)
        ? "Unknown"
        : _run.EvaluatorVersion;
    public string RerunRelation => string.IsNullOrWhiteSpace(_run.RerunOfRunId)
        ? "Original run"
        : $"Re-run of {_run.RerunOfRunId}";
    public string Evidence => _run.ComparisonEligible
        ? "Verified and eligible for comparison"
        : $"{(string.IsNullOrWhiteSpace(_run.Metadata.EvidenceStatus) ? "Unknown" : _run.Metadata.EvidenceStatus)} and excluded from trustworthy rankings";
    public string EvidenceStatusLabel => _run.RuntimeEvidence?.Status.ToString()
        ?? (string.IsNullOrWhiteSpace(_run.Metadata.EvidenceStatus) ? "Unknown" : _run.Metadata.EvidenceStatus);
    public string EvidenceReasonsLabel
    {
        get
        {
            var reasons = _run.RuntimeEvidence?.Reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Take(12)
                .ToArray() ?? [];
            return reasons.Length == 0
                ? "No structured reason was recorded. Runtime authority remains unverified."
                : string.Join(Environment.NewLine, reasons.Select(reason => $"• {reason}"));
        }
    }
    public bool HasEvidenceReasons => !_run.ComparisonEligible;
    public string ReconciliationSummary => BuildReconciliationSummary(_run);
    public string Score => _run.RankingScore.ToString("P0");
    public string PassRate => _run.PassRate.ToString("P0");
    public string Speed => $"median {_run.MedianApproxTokensPerSecond:F1} tok/s";
    public string InferenceEngine => _run.Metadata.InferenceEngineSummary;
    public List<BenchmarkResultViewModel> ResultSummaries { get; }

    public BenchmarkRunInfoViewModel(BenchmarkRun run, BenchmarkViewModel? parentVm = null)
    {
        _run = run;
        _parentVm = parentVm;
        ResultSummaries = run.Results.Select(r => new BenchmarkResultViewModel(r)).ToList();
    }

    private static string BuildReconciliationSummary(BenchmarkRun run)
    {
        var evidence = run.RuntimeEvidence;
        if (evidence is null)
            return "No runtime evidence envelope was persisted. Requested, launched, effective, and telemetry identities cannot be reconciled.";

        var fields = evidence.EffectiveFields.Count > 0
            ? string.Join(", ", evidence.EffectiveFields.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Take(12).Select(item => $"{item.Key}={item.Value}"))
            : evidence.EffectiveLaunch?.Fields is { Count: > 0 } observations
                ? string.Join(", ", observations.Take(12).Select(item => $"{item.Field}={item.EffectiveValue ?? "Unknown"}"))
                : "no effective fields recorded";
        return $"Requested {Short(evidence.RequestedConfigurationStableId)}; resolved {Short(evidence.ResolvedConfigurationStableId)}; "
            + $"launched {Short(evidence.LaunchedConfigurationStableId)}; effective {Short(evidence.EffectiveConfigurationStableId)}; "
            + $"telemetry {evidence.TelemetryProcessInstanceIds.Count} process id(s); fields: {fields}.";
    }

    private static string Short(string value) => string.IsNullOrWhiteSpace(value)
        ? "Unknown" : value.Length <= 16 ? value : value[..16];

    public async Task ExportRunAsync()
    {
        if (_parentVm is null) return;
        await _parentVm.ExportRunAsync(new BenchmarkRunViewModel(_run));
    }
}
