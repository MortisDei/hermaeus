using System.Text;
using Hermaeus.Rag;
using Hermaeus.Rag.Eval;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Core.Services;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class RagSourceViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public int    Rank    { get; init; }
    public string Title   { get; init; } = string.Empty;
    public string File    { get; init; } = string.Empty;
    public string Path    { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public float  Score   { get; init; }
    public string ScoreDisplay => $"{Score:F3}";

    /// <summary>Per-signal breakdown for "why did retrieval choose this chunk" (r6 1.6).</summary>
    public int    OutOfCount       { get; init; }
    public float? VectorScore      { get; init; }
    public float? KeywordScore     { get; init; }
    public float? RerankScore      { get; init; }
    public string MatchedTerm      { get; init; } = string.Empty;
    public int    MatchedTermCount { get; init; }
    public string PlainLanguageSummary { get; init; } = string.Empty;
    public string VectorScoreDisplay  => VectorScore is { } v ? v.ToString("F3") : string.Empty;
    public string KeywordScoreDisplay => KeywordScore is { } k ? k.ToString("F3") : string.Empty;
    public string RerankScoreDisplay  => RerankScore is { } r ? r.ToString("F3") : string.Empty;
    public bool   HasVectorScore  => VectorScore.HasValue;
    public bool   HasKeywordScore => KeywordScore.HasValue;
    public bool   HasRerankScore  => RerankScore.HasValue;

    public string CitationLabel => $"[{Rank}] {Title}";
    public string ShortCitationLabel => $"[{Rank}]";
    public string Snippet
    {
        get
        {
            var flat = Content.Replace('\n', ' ').Trim();
            return flat.Length > 220 ? flat[..217] + "..." : flat;
        }
    }
}

public sealed class RagIngestReportItemViewModel
{
    public RagIngestReportItemViewModel(DocumentIngestReport report)
    {
        Path = report.Path;
        Status = report.Status;
        Message = report.Message;
    }

    public string Path { get; }
    public DocumentIngestStatus Status { get; }
    public string Message { get; }
    public string StatusLabel => Status switch
    {
        DocumentIngestStatus.Added => "Added",
        DocumentIngestStatus.Replaced => "Replace",
        DocumentIngestStatus.SkippedUnchanged => "Skipped",
        DocumentIngestStatus.ReportOnly => "Report",
        DocumentIngestStatus.Error => "Error",
        _ => Status.ToString()
    };
}

public sealed class RagDatasetManagerItemViewModel
{
    public RagDatasetManagerItemViewModel(RagDataset dataset, string currentEmbeddingModel)
    {
        Dataset = dataset;
        Id = dataset.Id;
        Name = dataset.Name;
        Description = dataset.Description;
        ChunkCount = dataset.ChunkCount;
        CreatedAt = dataset.CreatedAt;
        EmbeddingModel = string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel) ? "unknown" : dataset.Config.EmbeddingModel;
        EmbeddingDimensions = dataset.Config.EmbeddingDimensions;
        CurrentEmbeddingModel = currentEmbeddingModel;
    }

    public RagDataset Dataset { get; }
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int ChunkCount { get; }
    public DateTime CreatedAt { get; }
    public string EmbeddingModel { get; }
    public int EmbeddingDimensions { get; }
    public string CurrentEmbeddingModel { get; }
    public int SourceCount { get; set; }
    public int MissingFiles { get; set; }
    public int StaleFiles { get; set; }
    public int DuplicateSources { get; set; }
    public IReadOnlyList<string> MissingSourcePaths { get; set; } = [];
    public string LastIngestLabel => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string DimensionsLabel => EmbeddingDimensions <= 0 ? "unknown dimensions" : $"{EmbeddingDimensions:N0} dimensions";
    public bool ReindexRequired => !string.IsNullOrWhiteSpace(CurrentEmbeddingModel)
        && !string.Equals(EmbeddingModel, "unknown", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(EmbeddingModel, CurrentEmbeddingModel, StringComparison.OrdinalIgnoreCase);
    public string ReindexStatus => ReindexRequired
        ? $"Reindex required: dataset uses {EmbeddingModel}, current provider is {CurrentEmbeddingModel}."
        : $"Embedding model: {EmbeddingModel}";
}

public partial class RagViewModel : ObservableObject
{
    private readonly RagQueryService _query;
    private readonly RagPipeline     _pipeline;
    private readonly RagEvalService  _eval;
    private readonly IToastService   _toasts;
    private readonly IRuntimeLogService _logs;
    private readonly ISettingsService _settings;
    private readonly ServicesViewModel? _services;
    private readonly XttsProcessManager? _xtts;
    private readonly KokoroProcessManager? _kokoro;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _ingestCts;
    private CancellationTokenSource? _evalCts;
    private RagDataset? _targetDatasetForIngest;

    public UiBoundCollection<RagDataset>       Datasets  { get; } = [];
    public UiBoundCollection<RagSourceViewModel> Sources  { get; } = [];
    public UiBoundCollection<RagSourceViewModel> VisibleCitationSources { get; } = [];
    public UiBoundCollection<RagEvalResultViewModel> EvalResults { get; } = [];
    public UiBoundCollection<RagIngestReportItemViewModel> IngestReportItems { get; } = [];
    public UiBoundCollection<RagDatasetManagerItemViewModel> DatasetManagerItems { get; } = [];

    [ObservableProperty] private RagDataset? _selectedDataset;
    [ObservableProperty] private string      _questionText    = string.Empty;
    [ObservableProperty] private string      _answerText      = string.Empty;
    [ObservableProperty] private bool        _isQuerying;
    [ObservableProperty] private bool        _isIngesting;
    [ObservableProperty] private string      _ingestPath      = string.Empty;
    [ObservableProperty] private bool        _ingestDryRun;
    [ObservableProperty] private IngestDuplicatePolicy _ingestPolicy = IngestDuplicatePolicy.Replace;
    [ObservableProperty] private string      _newDatasetName  = string.Empty;
    [ObservableProperty] private bool        _enableWebLoader;
    [ObservableProperty] private string      _webUrlList      = string.Empty;
    [ObservableProperty] private int         _webMaxPages     = 5;
    [ObservableProperty] private string      _statusMessage   = string.Empty;
    [ObservableProperty] private bool        _isError;
    [ObservableProperty] private int         _ingestDone;
    [ObservableProperty] private int         _ingestTotal;
    [ObservableProperty] private int         _ingestStageDone;
    [ObservableProperty] private int         _ingestStageTotal;
    [ObservableProperty] private string      _ingestStage     = string.Empty;
    [ObservableProperty] private string      _ingestProgressLabel = string.Empty;
    [ObservableProperty] private bool        _useParentChild;
    [ObservableProperty] private float       _groundingScore;
    [ObservableProperty] private bool        _hasAnswer;
    [ObservableProperty] private RagSourceViewModel? _selectedSource;
    [ObservableProperty] private bool        _showSourceInspector;
    [ObservableProperty] private string      _expandedQuery = string.Empty;
    [ObservableProperty] private string      _queryVariants = string.Empty;
    [ObservableProperty] private string      _plannerNotes = string.Empty;
    [ObservableProperty] private string      _contextPackingSummary = string.Empty;
    [ObservableProperty] private bool        _traceRefused;
    [ObservableProperty] private string      _refusalReason = string.Empty;
    [ObservableProperty] private string      _sourceOverflowLabel = string.Empty;
    [ObservableProperty] private bool        _hasSourceOverflow;
    [ObservableProperty] private string      _lastTraceId = string.Empty;
    [ObservableProperty] private long        _lastRetrievalLatencyMs;
    [ObservableProperty] private long        _lastTotalLatencyMs;
    [ObservableProperty] private string      _evalPath = string.Empty;
    [ObservableProperty] private bool        _isEvaluating;
    [ObservableProperty] private string      _evalStatus = string.Empty;
    [ObservableProperty] private int         _evalPassed;
    [ObservableProperty] private int         _evalTotal;
    [ObservableProperty] private double      _evalPassRate;
    [ObservableProperty] private RagEvalResultViewModel? _selectedEvalResult;
    [ObservableProperty] private RagDatasetManagerItemViewModel? _selectedDatasetManagerItem;
    [ObservableProperty] private string      _datasetManagerStatus = string.Empty;

    public event EventHandler? ScrollToBottom;
    public Action<string>? RequestCopyToClipboard { get; set; }
    public Func<RagDatasetManagerItemViewModel, Task<bool>>? RequestDeleteDatasetConfirmation { get; set; }
    public Func<RagDatasetManagerItemViewModel, Task<bool>>? RequestRemoveMissingSourcesConfirmation { get; set; }

    /// <summary>r21 3.3: "Open in chat" handoff, wired by MainWindowViewModel (which owns
    /// view switching and ChatViewModel access) - RagViewModel has no direct chat reference.</summary>
    public Action<RagDataset>? RequestOpenInChat { get; set; }
    public bool IsLocalIngest => !EnableWebLoader;

    public RagViewModel(RagQueryService query, RagPipeline pipeline, RagEvalService eval, IToastService toasts, IRuntimeLogService logs, ISettingsService settings, ServicesViewModel? services = null, XttsProcessManager? xtts = null, KokoroProcessManager? kokoro = null)
    {
        _query    = query;
        _pipeline = pipeline;
        _eval     = eval;
        _toasts   = toasts;
        _logs     = logs;
        _settings  = settings;
        _services = services;
        _xtts = xtts;
        _kokoro = kokoro;
    }

    public IEnumerable<IngestDuplicatePolicy> IngestPolicyOptions => Enum.GetValues<IngestDuplicatePolicy>();

    public async Task LoadDatasetsAsync()
    {
        try
        {
            var all = await _query.GetDatasetsAsync();
            Datasets.Clear();
            foreach (var d in all) Datasets.Add(d);
            SelectedDataset = Datasets.FirstOrDefault();
            await RefreshDatasetManagerAsync();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task QueryAsync()
    {
        if (SelectedDataset is null || string.IsNullOrWhiteSpace(QuestionText)) return;

        _cts = new CancellationTokenSource();
        IsQuerying = true;
        AnswerText = string.Empty;
        Sources.Clear();
        HasAnswer = false;
        IsError = false;
        StatusMessage = string.Empty;

        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG query started for dataset {SelectedDataset.Name}"));
            var opts = new RagQueryOptions(
                TopK: 5,
                UseParentChild: UseParentChild,
                ModelId: string.Empty);

            var answerBuilder = new StringBuilder();

            await foreach (var evt in _query.StreamQueryAsync(
                SelectedDataset.Id, QuestionText, opts, _cts.Token))
            {
                switch (evt.Kind)
                {
                    case RagStreamEventKind.Sources:
                        ApplySources(evt.Sources!);
                        break;
                    case RagStreamEventKind.Trace:
                        ApplyTrace(evt.Trace!);
                        break;
                    default:
                        answerBuilder.Append(evt.Text);
                        AnswerText = answerBuilder.ToString();
                        ScrollToBottom?.Invoke(this, EventArgs.Empty);
                        break;
                }
            }

            HasAnswer = !string.IsNullOrWhiteSpace(AnswerText);
            GroundingScore = RagQueryService.GroundingScore(
                AnswerText,
                string.Join(" ", Sources.Select(s => s.Content)));
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG query completed for dataset {SelectedDataset.Name}"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetError(ex.Message); }
        finally { IsQuerying = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void StopQuery() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanIngest))]
    private async Task IngestAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDatasetName)) return;
        if (EnableWebLoader && string.IsNullOrWhiteSpace(WebUrlList)) return;
        if (!EnableWebLoader && string.IsNullOrWhiteSpace(IngestPath)) return;

        // r10 01-rag-correctness.md 1.4: adding documents to an existing
        // dataset under a different current embedding model would mix
        // incompatible vectors in one dataset (old chunks are never
        // re-embedded by a plain add). Reindex is the only sanctioned way
        // to change a dataset's embedding model.
        if (_targetDatasetForIngest is not null)
        {
            var datasetModel = _targetDatasetForIngest.Config.EmbeddingModel;
            var currentModel = _settings.Settings.Rag.EmbeddingModel;
            if (!string.IsNullOrWhiteSpace(datasetModel)
                && !string.IsNullOrWhiteSpace(currentModel)
                && !string.Equals(datasetModel, currentModel, StringComparison.OrdinalIgnoreCase))
            {
                SetError($"Cannot add documents: '{_targetDatasetForIngest.Name}' was embedded with '{datasetModel}', current model is '{currentModel}'. Reindex the dataset first.");
                return;
            }
        }

        IsIngesting = true;
        IsError = false;
        StatusMessage = string.Empty;
        _ingestCts = new CancellationTokenSource();

        var suspension = new RagIngestServiceSuspension(_services, _xtts, _kokoro, _settings);
        Func<Task<IReadOnlyList<string>>>? restoreServices = null;

        try
        {
            restoreServices = await suspension.SuspendAsync();

            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG ingest started for dataset {NewDatasetName}"));

            var ds = RagIngestRequestBuilder.PrepareDataset(
                _targetDatasetForIngest,
                NewDatasetName,
                EnableWebLoader,
                IngestPath,
                WebUrlList,
                WebMaxPages,
                UseParentChild,
                _settings.Settings.Rag.EmbeddingModel);

            var progress = new Progress<IngestProgress>(p =>
            {
                IngestStage = p.Stage;
                IngestStageDone = p.Done;
                IngestStageTotal = p.Total;
                IngestDone = p.OverallTotal > 0 ? p.OverallDone : p.Done;
                IngestTotal = p.OverallTotal > 0 ? p.OverallTotal : p.Total;
                IngestProgressLabel = BuildIngestProgressLabel(p);
                StatusMessage = p.Detail;
            });

            IngestReport report;
            if (EnableWebLoader)
            {
                report = await _pipeline.IngestWebAsync(ds, progress, _ingestCts.Token, new IngestOptions { DryRun = IngestDryRun, DuplicatePolicy = IngestPolicy });
            }
            else
            {
                report = await _pipeline.IngestDirectoryAsync(ds, IngestPath, progress, _ingestCts.Token, new IngestOptions { DryRun = IngestDryRun, DuplicatePolicy = IngestPolicy });
            }

            // r10 01-rag-correctness.md 1.3: re-ingest into an already-queried
            // dataset must not keep serving the pre-ingest in-memory chunk
            // list until the app restarts.
            if (!IngestDryRun)
                _query.ClearCache(ds.Id);

            IngestReportItems.Clear();
            foreach (var document in report.Documents)
                IngestReportItems.Add(new RagIngestReportItemViewModel(document));

            // Prefer explicit health property on the report instead of sentinel documents.
            if (report.Health is not null)
            {
                var summary = RagIngestRequestBuilder.BuildHealthSummary(report.Health);
                IngestReportItems.Insert(0, new RagIngestReportItemViewModel(new DocumentIngestReport { Path = "Health summary", Status = DocumentIngestStatus.ReportOnly, Message = summary }));
            }

            await LoadDatasetsAsync();
            SelectedDataset = Datasets.FirstOrDefault(d => d.Name == ds.Name);
            await RefreshDatasetManagerAsync();
            NewDatasetName  = string.Empty;
            if (EnableWebLoader)
                WebUrlList = string.Empty;
            else
                IngestPath = string.Empty;
            _targetDatasetForIngest = null;
            StatusMessage   = "Ingestion complete.";
            _toasts.Show("RAG ingest complete", $"{report.Summary()}", ToastKind.Success);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG ingest complete for dataset {ds.Name}. {report.Summary()}"));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ingest cancelled.";
            _toasts.Show("RAG ingest cancelled", "Ingest was cancelled before completion.", ToastKind.Info, 5000);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                "RAG ingest cancelled."));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"RAG ingest failed: {ex.Message}"));
            _toasts.Show("RAG ingest failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            IsIngesting = false;
            IngestStage = string.Empty;
            IngestProgressLabel = string.Empty;
            _ingestCts?.Dispose();
            _ingestCts = null;
            if (restoreServices is not null)
            {
                var restoreErrors = await restoreServices();
                foreach (var error in restoreErrors)
                    _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                        $"RAG service restore failed: {error}"));
            }
        }
    }

    private static string BuildIngestProgressLabel(IngestProgress progress)
    {
        var current = progress.Total > 0
            ? $"{progress.Done:N0} / {progress.Total:N0}"
            : $"{progress.Done:N0}";

        if (progress.OverallTotal <= 0)
            return current;

        var overallPercent = Math.Clamp(progress.OverallDone / (double)progress.OverallTotal, 0, 1);
        var overall = $"{overallPercent:P0}";
        return string.IsNullOrWhiteSpace(progress.OverallDetail)
            ? $"{overall} overall - {current}"
            : $"{overall} overall - {progress.OverallDetail} - {current}";
    }

    [RelayCommand]
    private void StopIngest()
    {
        _ingestCts?.Cancel();
    }

    [RelayCommand]
    private void AddToDataset(RagDatasetManagerItemViewModel item)
    {
        if (item?.Dataset is null)
            return;

        _targetDatasetForIngest = item.Dataset;
        NewDatasetName = item.Dataset.Name;
        IngestPath = item.Dataset.LastIngestPath;
        EnableWebLoader = item.Dataset.Config.EnableWebLoader;

        // Scroll to ingest section (could be done via UI event if needed)
        StatusMessage = $"Ready to add documents to '{item.Dataset.Name}'. Select a directory or configure URLs below.";
    }

    /// <summary>r21 3.3: navigates to Chat and starts a new conversation with this
    /// dataset pre-attached. No reverse affordance ("open dataset" from chat) this round.</summary>
    [RelayCommand]
    private void OpenInChat(RagDatasetManagerItemViewModel item)
    {
        if (item?.Dataset is null)
            return;

        RequestOpenInChat?.Invoke(item.Dataset);
    }

    /// <summary>
    /// r12 03-runtime-vm-correctness.md 3.6: <see cref="RagIngestRequestBuilder.PrepareDataset"/>
    /// ignores <see cref="NewDatasetName"/> entirely once a target dataset is
    /// set, so a user who clicked "Add to dataset" and then edited the name
    /// box intending a *different* dataset silently ingested into the
    /// original one. Clear the target as soon as the box no longer matches
    /// it; the next ingest then creates a new dataset under the edited name.
    /// </summary>
    partial void OnNewDatasetNameChanged(string value)
    {
        if (_targetDatasetForIngest is not null
            && !string.Equals(value.Trim(), _targetDatasetForIngest.Name, StringComparison.Ordinal))
        {
            var previousTarget = _targetDatasetForIngest.Name;
            _targetDatasetForIngest = null;
            StatusMessage = string.IsNullOrWhiteSpace(value.Trim())
                ? string.Empty
                : $"Will create a new dataset named '{value.Trim()}' (no longer adding to '{previousTarget}').";
        }

        IngestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task DeleteDatasetAsync(RagDatasetManagerItemViewModel item)
    {
        if (item?.Dataset is null)
            return;

        var confirmed = RequestDeleteDatasetConfirmation is not null
            ? await RequestDeleteDatasetConfirmation(item)
            : false;
        
        if (!confirmed)
            return;

        try
        {
            await _query.DeleteDatasetAsync(item.Dataset.Id);

            // Update UI state
            if (SelectedDataset?.Id == item.Dataset.Id)
                SelectedDataset = null;
            if (_targetDatasetForIngest?.Id == item.Dataset.Id)
                _targetDatasetForIngest = null;

            await LoadDatasetsAsync();
            await RefreshDatasetManagerAsync();
            
            StatusMessage = $"Dataset '{item.Dataset.Name}' deleted.";
            _toasts.Show("Dataset deleted", $"'{item.Dataset.Name}' has been removed.", ToastKind.Info, 5000);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG dataset deleted by user: {item.Dataset.Name} ({item.Dataset.Id})"));
        }
        catch (Exception ex)
        {
            SetError($"Failed to delete dataset: {ex.Message}");
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"Failed to delete dataset: {ex.Message}"));
        }
    }

    /// <summary>
    /// r10 01-rag-correctness.md 1.5: a source file removed from the ingest
    /// folder stays in the dataset forever unless explicitly removed here.
    /// Never automatic (a temporarily unmounted drive must not silently
    /// shred a dataset), and always user-confirmed with the list of paths
    /// that will be dropped.
    /// </summary>
    [RelayCommand]
    private async Task RemoveMissingSourcesAsync(RagDatasetManagerItemViewModel item)
    {
        if (item?.Dataset is null || item.MissingSourcePaths.Count == 0)
            return;

        var confirmed = RequestRemoveMissingSourcesConfirmation is not null
            && await RequestRemoveMissingSourcesConfirmation(item);

        if (!confirmed)
            return;

        try
        {
            var remaining = await _query.RemoveMissingSourcesAsync(item.Dataset.Id, item.MissingSourcePaths);

            await LoadDatasetsAsync();
            await RefreshDatasetManagerAsync();

            StatusMessage = $"Removed {item.MissingSourcePaths.Count} missing source(s) from '{item.Dataset.Name}'; {remaining} chunk(s) remain.";
            _toasts.Show("Missing sources removed", StatusMessage, ToastKind.Info, 5000);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG missing sources removed by user from dataset {item.Dataset.Name} ({item.Dataset.Id}): {item.MissingSourcePaths.Count} source(s)."));
        }
        catch (Exception ex)
        {
            SetError($"Failed to remove missing sources: {ex.Message}");
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"Failed to remove missing sources: {ex.Message}"));
        }
    }

    /// <summary>
    /// r10 01-rag-correctness.md 1.4: re-embeds every chunk of a dataset
    /// with the current embedding model, from stored content only (no
    /// source files required). The only way a dataset's recorded embedding
    /// model changes; ingest refuses that mix instead.
    /// </summary>
    [RelayCommand]
    private async Task ReindexDatasetAsync(RagDatasetManagerItemViewModel item)
    {
        if (item?.Dataset is null || !item.ReindexRequired)
            return;

        IsIngesting = true;
        IsError = false;
        StatusMessage = string.Empty;
        _ingestCts = new CancellationTokenSource();
        var previousModel = string.IsNullOrWhiteSpace(item.Dataset.Config.EmbeddingModel) ? "unknown" : item.Dataset.Config.EmbeddingModel;
        var newModel = _settings.Settings.Rag.EmbeddingModel;

        // r12 03-runtime-vm-correctness.md 3.7: the pipeline needs
        // Config.EmbeddingModel set to the target model before it starts
        // embedding, but flipping it on the live, UI-bound dataset instance
        // would make ReindexRequired report false (defeating the r10 1.4
        // guard) the instant the run *starts*, not when it succeeds. Work
        // on a clone instead; the live instance is only ever replaced by a
        // fresh LoadDatasetsAsync reload, which only reflects what the
        // pipeline actually committed to disk.
        var workingDataset = item.Dataset.Clone();
        workingDataset.Config.EmbeddingModel = newModel;

        var suspension = new RagIngestServiceSuspension(_services, _xtts, _kokoro, _settings);
        Func<Task<IReadOnlyList<string>>>? restoreServices = null;

        try
        {
            restoreServices = await suspension.SuspendAsync();
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG reindex started for dataset {workingDataset.Name}: {previousModel} -> {newModel}"));

            var progress = new Progress<IngestProgress>(p =>
            {
                IngestStage = p.Stage;
                IngestStageDone = p.Done;
                IngestStageTotal = p.Total;
                IngestDone = p.Done;
                IngestTotal = p.Total;
                IngestProgressLabel = BuildIngestProgressLabel(p);
                StatusMessage = p.Detail;
            });

            var count = await _pipeline.ReindexDatasetAsync(workingDataset, progress, _ingestCts.Token);
            _query.ClearCache(workingDataset.Id);

            StatusMessage = $"Reindex complete: {count} chunk(s) re-embedded with {newModel}.";
            _toasts.Show("RAG reindex complete", $"{workingDataset.Name}: {count} chunk(s) re-embedded.", ToastKind.Success);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG reindex complete for dataset {workingDataset.Name}: {count} chunk(s), {previousModel} -> {newModel}."));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Reindex cancelled.";
            _toasts.Show("RAG reindex cancelled", "Reindex was cancelled before completion.", ToastKind.Info, 5000);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                "RAG reindex cancelled."));
        }
        catch (Exception ex)
        {
            SetError($"Reindex failed: {ex.Message}");
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"RAG reindex failed: {ex.Message}"));
            _toasts.Show("RAG reindex failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            IsIngesting = false;
            IngestStage = string.Empty;
            IngestProgressLabel = string.Empty;
            _ingestCts?.Dispose();
            _ingestCts = null;
            // Unconditional (success, cancellation, or failure): the working
            // copy above never touched the live dataset, so this is the only
            // thing that can bring the UI's view of the model/ReindexRequired
            // back in sync with whatever the pipeline actually committed.
            await LoadDatasetsAsync();
            await RefreshDatasetManagerAsync();
            if (restoreServices is not null)
            {
                var restoreErrors = await restoreServices();
                foreach (var error in restoreErrors)
                    _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                        $"RAG service restore failed: {error}"));
            }
        }
    }

    [RelayCommand]
    private void CopyAnswer()
    {
        if (!string.IsNullOrEmpty(AnswerText))
            RequestCopyToClipboard?.Invoke(AnswerText);
    }

    [RelayCommand]
    private void CopySource()
    {
        if (SelectedSource is not null)
            RequestCopyToClipboard?.Invoke(SelectedSource.Content);
    }

    [RelayCommand]
    private void CopySourcePath()
    {
        var path = SelectedSource?.Path;
        if (!string.IsNullOrWhiteSpace(path))
            RequestCopyToClipboard?.Invoke(path);
    }

    [RelayCommand]
    private async Task WarmCacheAsync()
    {
        if (SelectedDataset is null) return;
        StatusMessage = "Warming cache...";
        try
        {
            await _query.WarmCacheAsync(SelectedDataset.Id);
            StatusMessage = "Cache ready.";
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanRunEval))]
    private async Task RunRetrievalEvalAsync()
    {
        await RunEvalAsync(fullAnswer: false);
    }

    [RelayCommand(CanExecute = nameof(CanRunEval))]
    private async Task RunFullEvalAsync()
    {
        await RunEvalAsync(fullAnswer: true);
    }

    private async Task RunEvalAsync(bool fullAnswer)
    {
        if (SelectedDataset is null || string.IsNullOrWhiteSpace(EvalPath)) return;

        IsEvaluating = true;
        EvalResults.Clear();
        EvalStatus = "Starting eval...";
        // r10 02-rag-quality.md 2.6: evals used to run with CancellationToken.None,
        // so a long eval set could not be stopped from the UI once started.
        _evalCts = new CancellationTokenSource();
        try
        {
            var run = await _eval.RunAsync(
                SelectedDataset.Id,
                EvalPath,
                fullAnswer,
                new Progress<string>(s => EvalStatus = s),
                _evalCts.Token);

            EvalPassed = run.Passed;
            EvalTotal = run.Total;
            EvalPassRate = run.PassRate;
            EvalResults.Clear();
            foreach (var result in run.Results)
                EvalResults.Add(new RagEvalResultViewModel(result));
            SelectedEvalResult = EvalResults.FirstOrDefault();
            EvalStatus = $"Eval exported to eval-runs/{run.Id}.";
            _toasts.Show("RAG eval complete", $"{run.Passed}/{run.Total} passed.", run.PassRate >= 0.8 ? ToastKind.Success : ToastKind.Warning);
        }
        catch (OperationCanceledException)
        {
            EvalStatus = "Eval cancelled.";
            _toasts.Show("RAG eval cancelled", "Eval was cancelled before completion; no partial export was written.", ToastKind.Info, 5000);
        }
        catch (Exception ex)
        {
            EvalStatus = ex.Message;
            _toasts.Show("RAG eval failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            IsEvaluating = false;
            _evalCts?.Dispose();
            _evalCts = null;
        }
    }

    [RelayCommand]
    private void StopEval() => _evalCts?.Cancel();

    private bool CanQuery()  => !IsQuerying && !IsIngesting && SelectedDataset is not null
                                && !string.IsNullOrWhiteSpace(QuestionText);
    private bool CanIngest() => !IsIngesting && !IsQuerying
                                && !string.IsNullOrWhiteSpace(NewDatasetName)
                                && (EnableWebLoader
                                    ? !string.IsNullOrWhiteSpace(WebUrlList)
                                    : !string.IsNullOrWhiteSpace(IngestPath));
    private bool CanRunEval() => !IsEvaluating && SelectedDataset is not null && File.Exists(EvalPath);

    [RelayCommand]
    private async Task RefreshDatasetManagerAsync()
    {
        DatasetManagerItems.Clear();
        var datasets = await _query.GetDatasetsAsync();
        foreach (var dataset in datasets)
        {
            var item = new RagDatasetManagerItemViewModel(dataset, _settings.Settings.Rag.EmbeddingModel);
            try
            {
                // r10 02-rag-quality.md 2.5: this runs after every ingest,
                // delete, and app load; load only the columns health needs,
                // not full chunk content.
                var healthInfo = await _query.GetChunkHealthInfoForDatasetAsync(dataset.Id);
                var health = RagDatasetHealthService.Compute(healthInfo);
                item.SourceCount = health.SourceCount;
                item.DuplicateSources = health.DuplicateSources;
                item.MissingFiles = health.MissingFiles;
                item.StaleFiles = health.StaleFiles;
                item.MissingSourcePaths = health.MissingSourcePaths;
            }
            catch
            {
                item.SourceCount = 0;
            }

            DatasetManagerItems.Add(item);
        }

        DatasetManagerStatus = $"{DatasetManagerItems.Count} dataset(s), {DatasetManagerItems.Sum(i => i.SourceCount)} source(s).";
        SelectedDatasetManagerItem = DatasetManagerItems.FirstOrDefault(i => SelectedDataset is not null && i.Id == SelectedDataset.Id)
            ?? DatasetManagerItems.FirstOrDefault();
    }

    private void ApplySources(IReadOnlyList<RagTraceChunk> chunks)
    {
        Sources.Clear();
        SelectedSource = null;
        ShowSourceInspector = false;
        foreach (var chunk in chunks)
            Sources.Add(new RagSourceViewModel
            {
                Rank = chunk.Rank,
                Title = chunk.Title,
                File = chunk.File,
                Path = chunk.Path,
                Score = chunk.Score,
                Content = chunk.Content,
                OutOfCount = chunk.OutOfCount,
                VectorScore = chunk.VectorScore,
                KeywordScore = chunk.KeywordScore,
                RerankScore = chunk.RerankScore,
                MatchedTerm = chunk.MatchedTerm,
                MatchedTermCount = chunk.MatchedTermCount,
                PlainLanguageSummary = chunk.PlainLanguageSummary
            });
        SelectedSource = Sources.FirstOrDefault();
        if (SelectedSource is not null)
            SelectedSource.IsSelected = true;
        RefreshCitationOverflow();
    }

    private void ApplyTrace(RagTraceSummary trace)
    {
        LastTraceId = trace.Id;
        LastRetrievalLatencyMs = trace.RetrievalLatencyMs;
        LastTotalLatencyMs = trace.TotalLatencyMs;
        GroundingScore = trace.GroundingScore;
        if (trace.ExpandedQuery is not null)
            ExpandedQuery = trace.ExpandedQuery;
        if (trace.QueryVariants is not null)
            QueryVariants = trace.QueryVariants;
        if (trace.PlannerNotes is not null)
            PlannerNotes = trace.PlannerNotes;
        if (trace.ContextPackingSummary is not null)
            ContextPackingSummary = trace.ContextPackingSummary;
        if (trace.Refused is not null)
            TraceRefused = trace.Refused.Value;
        if (trace.RefusalReason is not null)
            RefusalReason = trace.RefusalReason;
    }

    private void SetError(string msg) { StatusMessage = msg; IsError = true; }

    [RelayCommand]
    private void SelectSource(RagSourceViewModel? source)
    {
        if (source is null) return;
        foreach (var s in Sources)
            s.IsSelected = false;

        source.IsSelected = true;
        SelectedSource = source;
        ShowSourceInspector = true;
    }

    [RelayCommand]
    private void ToggleSourceInspector()
    {
        ShowSourceInspector = !ShowSourceInspector;
        if (ShowSourceInspector && SelectedSource is null)
            SelectedSource = Sources.FirstOrDefault();
    }

    private void RefreshCitationOverflow()
    {
        VisibleCitationSources.Clear();
        foreach (var source in Sources.Take(3))
            VisibleCitationSources.Add(source);

        var overflow = Sources.Count - VisibleCitationSources.Count;
        HasSourceOverflow = overflow > 0;
        SourceOverflowLabel = overflow > 0 ? $"+{overflow}" : string.Empty;
    }

    partial void OnQuestionTextChanged(string value) => QueryCommand.NotifyCanExecuteChanged();
    partial void OnSelectedDatasetChanged(RagDataset? value)
    {
        QueryCommand.NotifyCanExecuteChanged();
        RunRetrievalEvalCommand.NotifyCanExecuteChanged();
        RunFullEvalCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsQueryingChanged(bool value) => QueryCommand.NotifyCanExecuteChanged();
    partial void OnIngestPathChanged(string value) => IngestCommand.NotifyCanExecuteChanged();
    partial void OnWebUrlListChanged(string value) => IngestCommand.NotifyCanExecuteChanged();
    partial void OnEnableWebLoaderChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLocalIngest));
        IngestCommand.NotifyCanExecuteChanged();
    }
    partial void OnEvalPathChanged(string value)
    {
        RunRetrievalEvalCommand.NotifyCanExecuteChanged();
        RunFullEvalCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsEvaluatingChanged(bool value)
    {
        RunRetrievalEvalCommand.NotifyCanExecuteChanged();
        RunFullEvalCommand.NotifyCanExecuteChanged();
    }
}

public sealed class RagEvalResultViewModel
{
    public RagEvalResultViewModel(RagEvalResult result)
    {
        CaseId = result.CaseId;
        Question = result.Question;
        RetrievalHit = result.RetrievalHit;
        KeywordHit = result.KeywordHit;
        RefusalCorrect = result.RefusalCorrect;
        Passed = result.Passed;
        LatencyMs = result.LatencyMs;
        GroundingScore = result.GroundingScore;
        Answer = result.Answer;
        Notes = result.Notes;
        RetrievedSummary = string.Join("  ", result.Retrieved.Take(3).Select(r => $"[{r.Rank}] {r.Title}"));
    }

    public string CaseId { get; }
    public string Question { get; }
    public bool RetrievalHit { get; }
    public bool KeywordHit { get; }
    public bool RefusalCorrect { get; }
    public bool Passed { get; }
    public double LatencyMs { get; }
    public float GroundingScore { get; }
    public string Answer { get; }
    public string Notes { get; }
    public string RetrievedSummary { get; }
    public string StatusLabel => Passed ? "PASS" : "FAIL";
    public string LatencyDisplay => $"{LatencyMs:F0} ms";
}
