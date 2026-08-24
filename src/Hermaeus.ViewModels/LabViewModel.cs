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

    public LabViewModel(IEmpiricalExperienceStore store, IToastService toasts)
    {
        _store = store;
        _toasts = toasts;
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

    public bool HasSelection => SelectedExperience is not null;
    public Func<EmpiricalExperience, Task<bool>>? ConfirmRemoval { get; set; }

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

    private static string? Choice(string value) => string.Equals(value, "All", StringComparison.Ordinal) ? null : value;
    private static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static T? ParseChoice<T>(string value) where T : struct, Enum =>
        string.Equals(value, "All", StringComparison.Ordinal) ? null : Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
