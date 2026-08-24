using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.ViewModels;

public partial class ExperienceRowViewModel : ViewModelBase
{
    public ExperienceRowViewModel(EmpiricalExperience experience) => Experience = experience;
    public EmpiricalExperience Experience { get; }
    public string Id => Experience.Id;
    public string Domain => Experience.Domain;
    public string OutcomeLabel => Experience.Outcome.Outcome.ToString();
    public string OriginLabel => Experience.Provenance.Count == 0
        ? "Unknown"
        : string.Join(", ", Experience.Provenance.Select(p => p.Source.EvidenceOrigin).Distinct());
    public string ScopeLabel => Experience.ProjectId ?? Experience.WorkspaceFingerprint ?? "Unscoped";
    public string CreatedLabel => Experience.CreatedAtUtc.ToLocalTime().ToString("g");
    public string StatusLabel => Experience.Status.ToString();
    [ObservableProperty] private bool _isExportSelected;
}

public partial class LabViewModel : ViewModelBase
{
    private readonly IEmpiricalExperienceStore _store;
    private readonly IToastService _toasts;
    private readonly ILabExperimentService? _experiments;
    private readonly ISettingsService? _settings;

    public LabViewModel(IEmpiricalExperienceStore store, IToastService toasts)
        : this(store, toasts, null, null)
    {
    }

    public LabViewModel(IEmpiricalExperienceStore store, IToastService toasts,
        ILabExperimentService? experiments, ISettingsService? settings)
    {
        _store = store;
        _toasts = toasts;
        _experiments = experiments;
        _settings = settings;
        SelectedServer = settings?.Settings.ManagedServers.FirstOrDefault(server => !server.EmbeddingsMode);
        if (SelectedServer is not null)
            CandidateContextSize = SelectedServer.ContextSize;
    }

    public UiBoundCollection<ExperienceRowViewModel> Experiences { get; } = [];
    public IReadOnlyList<string> DomainOptions { get; } = ["All", .. EmpiricalExperienceDomains.Initial.OrderBy(x => x)];
    public IReadOnlyList<string> OutcomeOptions { get; } = ["All", .. Enum.GetNames<NormalizedOutcome>()];
    public IReadOnlyList<string> OriginOptions { get; } = ["All", .. Enum.GetNames<EvidenceOrigin>()];
    public IReadOnlyList<string> StatusOptions { get; } = ["All", .. Enum.GetNames<EmpiricalExperienceStatus>()];

    [ObservableProperty] private string _domainFilter = "All";
    [ObservableProperty] private string _outcomeFilter = "All";
    [ObservableProperty] private string _originFilter = "All";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _projectFilter = string.Empty;
    [ObservableProperty] private string _workspaceFilter = string.Empty;
    [ObservableProperty] private string _modelFilter = string.Empty;
    [ObservableProperty] private string _runtimeFilter = string.Empty;
    [ObservableProperty] private DateTimeOffset? _createdFrom;
    [ObservableProperty] private DateTimeOffset? _createdTo;
    [ObservableProperty] private ExperienceRowViewModel? _selectedExperience;
    [ObservableProperty] private string _correctionOutcome = nameof(NormalizedOutcome.Unknown);
    [ObservableProperty] private string _correctionDetail = string.Empty;
    [ObservableProperty] private string _exportJson = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private ServerConfig? _selectedServer;
    [ObservableProperty] private string _experimentName = "Isolated baseline";
    [ObservableProperty] private int _candidateContextSize = 4096;
    [ObservableProperty] private string _definitionPreview = string.Empty;
    [ObservableProperty] private string _runStatus = "Not started";
    [ObservableProperty] private string _runtimeIsolation = "No Lab runtime is active.";
    [ObservableProperty] private string _comparisonSummary = string.Empty;
    [ObservableProperty] private string _applyReviewSummary = string.Empty;
    [ObservableProperty] private bool _isRunActive;

    private LabRunSnapshot? _currentRun;
    private LabApplyReview? _applyReview;

    public bool HasSelection => SelectedExperience is not null;
    public IReadOnlyList<ServerConfig> ConfiguredServers => _settings?.Settings.ManagedServers.Where(server => !server.EmbeddingsMode).ToArray() ?? [];
    public Func<EmpiricalExperience, Task<bool>>? ConfirmRemoval { get; set; }
    public Func<LabApplyReview, Task<bool>>? ConfirmApply { get; set; }

    partial void OnSelectedServerChanged(ServerConfig? value)
    {
        if (value is not null) CandidateContextSize = value.ContextSize;
        OnPropertyChanged(nameof(ConfiguredServers));
    }

    partial void OnSelectedExperienceChanged(ExperienceRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (value is null) return;
        CorrectionOutcome = value.Experience.Outcome.Outcome.ToString();
        CorrectionDetail = value.Experience.Outcome.Detail;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var query = new EmpiricalExperienceQuery
            {
                Domain = Choice(DomainFilter), ProjectId = Text(ProjectFilter), WorkspaceFingerprint = Text(WorkspaceFilter),
                ModelFingerprint = Text(ModelFilter), RuntimeFingerprint = Text(RuntimeFilter),
                Outcome = ParseChoice<NormalizedOutcome>(OutcomeFilter), Origin = ParseChoice<EvidenceOrigin>(OriginFilter),
                Status = ParseChoice<EmpiricalExperienceStatus>(StatusFilter),
                CreatedFromUtc = CreatedFrom?.UtcDateTime, CreatedToUtc = CreatedTo?.UtcDateTime, Limit = 500
            };
            var rows = await _store.QueryAsync(query);
            Experiences.Clear();
            foreach (var row in rows) Experiences.Add(new ExperienceRowViewModel(row));
            SelectedExperience = Experiences.FirstOrDefault();
            StatusMessage = rows.Count == 0 ? "No evidence matches these filters." : $"{rows.Count} evidence record(s).";
        }
        catch (Exception ex) { _toasts.Show("Could not load evidence", ex.Message, ToastKind.Error, 5000); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CorrectSelectedAsync()
    {
        if (SelectedExperience?.Experience is not { } prior || !Enum.TryParse<NormalizedOutcome>(CorrectionOutcome, out var outcome)) return;
        try
        {
            var provenance = prior.Provenance.Take(15).Concat([
                new EmpiricalExperienceProvenance($"correction:{prior.Id}", new SourceReference(
                    ProvenanceKind.Experience, "User correction", prior.Id, EvidenceOrigin: EvidenceOrigin.UserProvided))
            ]).ToArray();
            await _store.CorrectAsync(prior.Id, new EmpiricalExperienceDraft
            {
                SchemaVersion = prior.SchemaVersion, Domain = prior.Domain, ProjectId = prior.ProjectId,
                WorkspaceFingerprint = prior.WorkspaceFingerprint, ContextJson = prior.ContextJson, ActionJson = prior.ActionJson,
                RuntimeFingerprint = prior.RuntimeFingerprint, ModelFingerprint = prior.ModelFingerprint, Provenance = provenance,
                Outcome = NormalizedToolOutcome.Create(outcome, "user-correction", CorrectionDetail)
            });
            await RefreshAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not correct evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedExperience?.Experience is not { } selected) return;
        if (ConfirmRemoval is null || !await ConfirmRemoval(selected)) return;
        try { await _store.RemoveAsync(selected.Id); await RefreshAsync(); }
        catch (Exception ex) { _toasts.Show("Could not remove evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var ids = Experiences.Where(x => x.IsExportSelected).Select(x => x.Id).ToArray();
        if (ids.Length == 0 && SelectedExperience is not null) ids = [SelectedExperience.Id];
        if (ids.Length == 0) return;
        try { ExportJson = await _store.ExportAsync(ids); StatusMessage = $"Prepared {ids.Length} record(s) for copy or save."; }
        catch (Exception ex) { _toasts.Show("Could not export evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task FreezeAndStartAsync()
    {
        if (_experiments is null || SelectedServer is null || IsRunActive) return;
        IsBusy = true;
        try
        {
            var baseline = ConfigurationFrom(SelectedServer, "baseline", "Baseline");
            var candidate = baseline with { Id = "candidate-1", Label = "Candidate", ContextSize = CandidateContextSize };
            var definition = await _experiments.CreateDefinitionAsync(
                ExperimentName, "isolated-runtime-v1", SelectedServer, baseline, [candidate], 1,
                LabCorrectnessRequirement.ExactEquivalence);
            DefinitionPreview = definition.CanonicalJson();
            _currentRun = await _experiments.StartAsync(definition, SelectedServer);
            RunStatus = _currentRun.Status.ToString();
            IsRunActive = _currentRun.Status == LabRunStatus.Running;
            RuntimeIsolation = _currentRun.TemporaryPort is int port
                ? $"Dedicated loopback runtime on 127.0.0.1:{port}. Saved Services settings are unchanged."
                : _currentRun.Failures.FirstOrDefault() ?? "The isolated runtime did not start.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _toasts.Show("Could not start Lab run", ex.Message, ToastKind.Error, 5000);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CompleteRunAsync()
    {
        if (_experiments is null || _currentRun?.Status != LabRunStatus.Running) return;
        try
        {
            var fingerprint = _currentRun.Definition.ProfileFingerprint;
            var observation = new LabObservation
            {
                RunId = _currentRun.Id, ConfigurationId = _currentRun.Definition.Baseline.Id,
                CaseId = "runtime-start", Repetition = 0, MetricId = "runtime.ready", Value = 1,
                Unit = "boolean", Source = "isolated-runtime-health", Trust = "TrustedRuntime",
                RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
                HardwareFingerprint = fingerprint.Hardware.StableId,
                ConfigurationFingerprint = _currentRun.Definition.ConfigurationFingerprints[_currentRun.Definition.Baseline.Id]
            };
            _currentRun = await _experiments.CompleteAsync(_currentRun.Id, [observation], []);
            ShowCompletedRun(_currentRun);
            await RefreshAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not complete Lab run", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task CancelRunAsync()
    {
        if (_experiments is null || _currentRun is null || !IsRunActive) return;
        try
        {
            _currentRun = await _experiments.CancelAsync(_currentRun.Id);
            ShowCompletedRun(_currentRun);
            await RefreshAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not cancel Lab run", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private void ReviewApply()
    {
        if (_experiments is null || _currentRun is null) return;
        try
        {
            _applyReview = _experiments.CreateApplyReview(_currentRun.Id, "candidate-1");
            ApplyReviewSummary = _applyReview.CanApply
                ? string.Join(Environment.NewLine, _applyReview.Changes.Select(change => $"{change.Field}: {change.CurrentValue} -> {change.ProposedValue}"))
                : _applyReview.RefusalReason;
        }
        catch (Exception ex) { _toasts.Show("Could not review Lab result", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task ConfirmApplyAsync()
    {
        if (_experiments is null || _applyReview?.CanApply != true || ConfirmApply is null
            || !await ConfirmApply(_applyReview)) return;
        try
        {
            await _experiments.ApplyAsync(_applyReview);
            ApplyReviewSummary = "Reviewed fields were saved through the normal Settings flow.";
        }
        catch (Exception ex) { _toasts.Show("Could not apply Lab result", ex.Message, ToastKind.Error, 5000); }
    }

    private void ShowCompletedRun(LabRunSnapshot run)
    {
        RunStatus = run.Status.ToString();
        IsRunActive = false;
        RuntimeIsolation = "The dedicated Lab runtime is stopped and its ownership record is cleaned up.";
        ComparisonSummary = run.Comparisons.Count == 0
            ? "No controlled comparison was produced."
            : string.Join(Environment.NewLine, run.Comparisons.Select(comparison => comparison.CanShowHeadlineDelta
                ? $"{comparison.CandidateConfigurationId}: controlled and correctness-gated"
                : $"{comparison.CandidateConfigurationId}: {comparison.RefusalReason}"));
    }

    private static LabConfiguration ConfigurationFrom(ServerConfig source, string id, string label) => new()
    {
        Id = id, Label = label, ContextSize = source.ContextSize, GpuLayers = source.GpuLayers,
        Threads = source.Threads, PromptThreads = source.PromptThreads, Slots = source.Slots,
        KvCacheTypeK = source.KvCacheTypeK, KvCacheTypeV = source.KvCacheTypeV,
        FlashAttention = source.FlashAttention, CpuMoeLayers = source.CpuMoeLayers,
        ExtraArgumentsSha256 = string.IsNullOrWhiteSpace(source.ExtraArgs)
            ? string.Empty : LabCanonicalJson.Hash(source.ExtraArgs)
    };

    private static string? Choice(string value) => string.Equals(value, "All", StringComparison.Ordinal) ? null : value;
    private static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static T? ParseChoice<T>(string value) where T : struct, Enum =>
        string.Equals(value, "All", StringComparison.Ordinal) ? null : Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
