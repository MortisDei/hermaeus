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
    public string SourceId { get; init; } = string.Empty;
    public string SourceRevisionId { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
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
    public string CitationIdentity => string.IsNullOrWhiteSpace(SourceRevisionId)
        ? "Legacy or unversioned source"
        : $"Revision {SourceRevisionId} · {ContentHash[..Math.Min(ContentHash.Length, 12)]}";
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
    public IReadOnlyList<RagDatasetGeneration> GenerationHistory { get; set; } = [];
    public string LastIngestLabel => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string GenerationHistoryLabel => GenerationHistory.Count switch
    {
        0 => "Generations: none",
        1 => "Generations: 1 published",
        var count => $"Generations: {count} published, latest is current"
    };

    /// <summary>doc 03 3.5: watched-source surfacing on the Dataset Manager card.</summary>
    public int WatchedSourceCount => Dataset.Config.WatchedSources.Count;
    public RagRefreshPlan? DriftPlan { get; set; }
    public string WatchedLastRefreshLabel
    {
        get
        {
            var last = Dataset.Config.WatchedSources.Where(w => w.LastRefreshUtc.HasValue).Select(w => w.LastRefreshUtc!.Value).DefaultIfEmpty().Max();
            return last == default ? "never refreshed" : $"checked {last.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }
    public string WatchedDriftSummary => DriftPlan is null
        ? string.Empty
        : DriftPlan.HasDrift
            ? $"{DriftPlan.ChangedFiles.Count} changed, {DriftPlan.NewFiles.Count} new, {DriftPlan.MissingFiles.Count} missing"
            : "up to date";
    public string DimensionsLabel => EmbeddingDimensions <= 0 ? "unknown dimensions" : $"{EmbeddingDimensions:N0} dimensions";

    // ── r27 02-retrieval-that-scales.md 2.7: the ceiling, before you hit it ──

    /// <summary>Bytes this dataset's semantic scan index occupies in memory, exact arithmetic over chunk count and dimension.</summary>
    public long ScanIndexBytes { get; set; }

    /// <summary>The in-memory budget every dataset's scan index shares.</summary>
    public long ScanIndexBudgetBytes { get; set; }

    /// <summary>True when this dataset's index currently fits, and is therefore scanned from memory rather than from storage.</summary>
    public bool ScanIndexCached => ScanIndexBudgetBytes > 0 && ScanIndexBytes > 0 && ScanIndexBytes <= ScanIndexBudgetBytes;

    /// <summary>
    /// The same factual register as the rest of the dataset health line: a size
    /// against a budget, and what happens above it. Not a warning and not a
    /// recommendation.
    /// </summary>
    public string ScanIndexLabel
    {
        get
        {
            if (ScanIndexBudgetBytes <= 0 || EmbeddingDimensions <= 0)
                return "Search index: size unknown until this dataset is indexed";

            var used = FormatMib(ScanIndexBytes);
            var budget = FormatMib(ScanIndexBudgetBytes);
            return ScanIndexCached
                ? $"Search index: {used} of {budget} in memory"
                : $"Search index: {used}, over the {budget} memory budget; queries read from storage and are slower";
        }
    }

    private static string FormatMib(long bytes) =>
        bytes >= 1024L * 1024L ? $"{bytes / 1024d / 1024d:0.#} MiB" : $"{Math.Max(bytes / 1024d, 0):0.#} KiB";
    public bool ReindexRequired => !string.IsNullOrWhiteSpace(CurrentEmbeddingModel)
        && !string.Equals(EmbeddingModel, "unknown", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(EmbeddingModel, CurrentEmbeddingModel, StringComparison.OrdinalIgnoreCase);
    public string ReindexStatus => ReindexRequired
        ? $"Reindex required: dataset uses {EmbeddingModel}, current provider is {CurrentEmbeddingModel}."
        : $"Embedding model: {EmbeddingModel}";
}

/// <summary>
/// One dataset that can be included in the next RAG question. This is kept
/// separate from the manager's SelectedDataset because ingest, reindex and
/// evaluation still operate on exactly one dataset at a time.
/// </summary>
public sealed class RagDatasetQueryOptionViewModel : ObservableObject
{
    private bool _isIncluded;

    public RagDatasetQueryOptionViewModel(RagDataset dataset, bool isIncluded)
    {
        Dataset = dataset;
        _isIncluded = isIncluded;
    }

    public RagDataset Dataset { get; }
    public string Id => Dataset.Id;
    public string Name => Dataset.Name;
    public int ChunkCount => Dataset.ChunkCount;
    public string ChunkLabel => $"{ChunkCount} chunk{(ChunkCount == 1 ? "" : "s")}";

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value)
                return;
            _isIncluded = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    internal Action? SelectionChanged { get; set; }
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
    private readonly IActivityRecorder? _activity;
    private readonly WatchedSourceService? _watchedSources;
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
    public UiBoundCollection<RagDatasetQueryOptionViewModel> QueryDatasetOptions { get; } = [];

    public bool HasDatasets => Datasets.Count > 0;
    public const string QuerySubview = "query";
    public const string SourcesSubview = "sources";
    public const string DiagnosticsSubview = "diagnostics";

    [ObservableProperty] private string _activeSubview = QuerySubview;
    public bool IsQuerySubview => ActiveSubview == QuerySubview;
    public bool IsSourcesSubview => ActiveSubview == SourcesSubview;
    public bool IsDiagnosticsSubview => ActiveSubview == DiagnosticsSubview;

    partial void OnActiveSubviewChanged(string value)
    {
        OnPropertyChanged(nameof(IsQuerySubview));
        OnPropertyChanged(nameof(IsSourcesSubview));
        OnPropertyChanged(nameof(IsDiagnosticsSubview));
    }

    [RelayCommand]
    private void ShowQuerySubview() => ActiveSubview = QuerySubview;

    [RelayCommand]
    private void ShowSourcesSubview() => ActiveSubview = SourcesSubview;

    [RelayCommand]
    private void ShowDiagnosticsSubview() => ActiveSubview = DiagnosticsSubview;
    public bool ShowBundledHelpOnboarding => !HasDatasets && Directory.Exists(BundledHelpDirectory);
    public string BundledHelpDirectory => ResolveBundledHelpDirectory();
    public string BundledHelpStatus => ShowBundledHelpOnboarding
        ? "Create a searchable, version-local help dataset from the Hermaeus documentation bundled with this build."
        : string.Empty;

    /// <summary>The settings service is exposed for the desktop input handler,
    /// matching ChatView's shared Enter-to-send policy.</summary>
    public ISettingsService Settings => _settings;

    [ObservableProperty] private RagDataset? _selectedDataset;
    [ObservableProperty] private string      _questionText    = string.Empty;

    /// <summary>r24 doc 01 1.6: pre-selects the newly activated project's dataset for
    /// the next query or add-documents action, but only when nothing is already
    /// selected - never overrides a dataset the user is actively working in.</summary>
    public void SetDefaultDatasetFromProject(string datasetId)
    {
        if (SelectedDataset is not null || string.IsNullOrWhiteSpace(datasetId))
            return;

        var match = Datasets.FirstOrDefault(d => d.Id == datasetId);
        if (match is not null)
        {
            SelectedDataset = match;
            SetQueryDatasetIncluded(match.Id, true);
        }
    }

    public string QueryDatasetSelectionLabel
    {
        get
        {
            var selected = QueryDatasetOptions.Where(option => option.IsIncluded).Select(option => option.Name).ToList();
            if (selected.Count == 0 && QueryDatasetOptions.Count == 0 && SelectedDataset is not null)
                return $"Using 1 dataset: {SelectedDataset.Name}";
            return selected.Count == 0
                ? "Select at least one dataset before asking."
                : $"Using {selected.Count} dataset{(selected.Count == 1 ? "" : "s")}: {string.Join(", ", selected)}";
        }
    }
    [ObservableProperty] private string      _answerText      = string.Empty;

    /// <summary>
    /// The question this answer was given to. The box clears on send, so
    /// without this the panel would show an answer with nothing saying what was
    /// asked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAskedQuestion))]
    private string _askedQuestion = string.Empty;

    public bool HasAskedQuestion => !string.IsNullOrWhiteSpace(AskedQuestion);
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
    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }
    public Func<RagDatasetManagerItemViewModel, Task<bool>>? RequestDeleteDatasetConfirmation { get; set; }
    public Func<RagDatasetManagerItemViewModel, Task<bool>>? RequestRemoveMissingSourcesConfirmation { get; set; }

    /// <summary>doc 03 3.3: a separate confirmation from RequestRemoveMissingSourcesConfirmation
    /// - confirming new/changed ingest is not confirming a deletion.</summary>
    public Func<RagDatasetManagerItemViewModel, RagRefreshPlan, Task<bool>>? RequestConfirmWatchedRefresh { get; set; }
    public Func<Task<string?>>? RequestWatchedFolderPicker { get; set; }

    /// <summary>r21 3.3: "Open in chat" handoff, wired by MainWindowViewModel (which owns
    /// view switching and ChatViewModel access) - RagViewModel has no direct chat reference.</summary>
    public Action<RagDataset>? RequestOpenInChat { get; set; }
    public bool IsLocalIngest => !EnableWebLoader;

    /// <summary>
    /// Set by the DI root to the model Chat currently has selected. The RAG
    /// panel has no model picker of its own, and the settings default is often
    /// unset, so without this Ask had no model to write an answer with.
    /// </summary>
    public Func<string>? ChatModelProvider { get; set; }

    public RagViewModel(RagQueryService query, RagPipeline pipeline, RagEvalService eval, IToastService toasts, IRuntimeLogService logs, ISettingsService settings, ServicesViewModel? services = null, XttsProcessManager? xtts = null, KokoroProcessManager? kokoro = null, IActivityRecorder? activity = null, WatchedSourceService? watchedSources = null)
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
        _activity = activity;
        _watchedSources = watchedSources;
    }

    public IEnumerable<IngestDuplicatePolicy> IngestPolicyOptions => Enum.GetValues<IngestDuplicatePolicy>();

    public async Task LoadDatasetsAsync()
    {
        try
        {
            var previousSelectedId = SelectedDataset?.Id;
            var all = await _query.GetDatasetsAsync();
            Datasets.Clear();
            foreach (var d in all) Datasets.Add(d);
            OnPropertyChanged(nameof(HasDatasets));
            OnPropertyChanged(nameof(ShowBundledHelpOnboarding));
            OnPropertyChanged(nameof(BundledHelpStatus));
            SelectedDataset = Datasets.FirstOrDefault(d => d.Id == previousSelectedId)
                ?? Datasets.FirstOrDefault();
            QueryDatasetOptions.Clear();
            var savedIncludedIds = _settings.Settings.Rag.LastQueryDatasetIds;
            var includedIds = savedIncludedIds is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : savedIncludedIds
                    .Where(id => Datasets.Any(dataset => dataset.Id == id))
                    .ToHashSet(StringComparer.Ordinal);
            foreach (var dataset in Datasets)
            {
                var option = new RagDatasetQueryOptionViewModel(dataset, includedIds.Contains(dataset.Id))
                {
                    SelectionChanged = OnQueryDatasetSelectionChanged
                };
                QueryDatasetOptions.Add(option);
            }
            OnPropertyChanged(nameof(QueryDatasetSelectionLabel));
            QueryCommand.NotifyCanExecuteChanged();
            await RefreshDatasetManagerAsync();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task QueryAsync()
    {
        var selectedDatasets = QueryDatasetOptions.Where(option => option.IsIncluded).ToList();
        var datasetIds = selectedDatasets.Count > 0
            ? selectedDatasets.Select(option => option.Id).ToArray()
            : SelectedDataset is not null && QueryDatasetOptions.Count == 0
                ? [SelectedDataset.Id]
                : [];
        var datasetNames = selectedDatasets.Count > 0
            ? selectedDatasets.Select(option => option.Name).ToArray()
            : SelectedDataset is not null && QueryDatasetOptions.Count == 0
                ? [SelectedDataset.Name]
                : [];
        if (string.IsNullOrWhiteSpace(QuestionText)) return;
        if (datasetIds.Length == 0)
        {
            StatusMessage = "Choose at least one dataset before asking a question.";
            IsError = false;
            return;
        }

        // The box empties on send, the way the chat composer does, so a sent
        // question never looks like an unsent one. The text is not lost: it is
        // shown above the answer it produced, and put back in the box if the
        // query failed, because then there is something to edit and retry.
        var question = QuestionText.Trim();
        QuestionText = string.Empty;
        AskedQuestion = question;

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
                $"RAG query started for dataset(s) {string.Join(", ", datasetNames)}"));
            // Ask used to pass an empty model id unconditionally and let the
            // query service fall back to Llm.DefaultModel. That setting is
            // empty on any install where the user only ever picked a model from
            // the Chat dropdown, so Ask failed with a routing error naming an
            // empty model. Use whatever Chat is actually using; the query
            // service still falls back to the configured default, and now says
            // so plainly when there is no model at all.
            var opts = new RagQueryOptions(
                TopK: 5,
                UseParentChild: UseParentChild,
                ModelId: ChatModelProvider?.Invoke() ?? string.Empty);

            var answerBuilder = new StringBuilder();

            await foreach (var evt in _query.StreamQueryAsync(
                datasetIds, question, opts, _cts.Token))
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
                $"RAG query completed for dataset(s) {string.Join(", ", datasetNames)}"));
        }
        catch (OperationCanceledException) { RestoreQuestion(question); }
        catch (Exception ex) { SetError(ex.Message); RestoreQuestion(question); }
        finally { IsQuerying = false; _cts?.Dispose(); _cts = null; }
    }

    /// <summary>
    /// Puts a question back in the box after a failed or cancelled query, so the
    /// user can edit and retry instead of retyping. Never overwrites something
    /// they have started typing in the meantime.
    /// </summary>
    private void RestoreQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(QuestionText))
        {
            QuestionText = question;
        }

        AskedQuestion = string.Empty;
    }

    [RelayCommand]
    private void StopQuery() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanCreateBundledHelpDataset))]
    private async Task CreateBundledHelpDatasetAsync()
    {
        if (!CanCreateBundledHelpDataset())
            return;

        var previousName = NewDatasetName;
        var previousPath = IngestPath;
        var previousWebLoader = EnableWebLoader;
        var previousPolicy = IngestPolicy;
        try
        {
            NewDatasetName = "Hermaeus Help";
            IngestPath = BundledHelpDirectory;
            EnableWebLoader = false;
            IngestPolicy = IngestDuplicatePolicy.Replace;
            await IngestAsync();
        }
        finally
        {
            NewDatasetName = previousName;
            IngestPath = previousPath;
            EnableWebLoader = previousWebLoader;
            IngestPolicy = previousPolicy;
        }
    }

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

            // Ingest runs on the thread pool, not the UI thread. The pipeline is
            // async but its expensive parts are synchronous and CPU-bound
            // (ParagraphChunker's regex work per file, and Bm25Scorer.BuildStats
            // tokenising every stored chunk at the end), and nothing in
            // Hermaeus.Rag uses ConfigureAwait(false), so every continuation
            // resumed on the UI thread and ran that work there. A 1,759 file
            // ingest producing 12,794 chunks froze the window for minutes,
            // worst around the final "Building BM25 stats" phase, which is why
            // it looked like it hung near the end rather than throughout.
            //
            // Progress<T> was constructed on the UI thread above, so its
            // callbacks still marshal back correctly on their own.
            var ingestOptions = new IngestOptions { DryRun = IngestDryRun, DuplicatePolicy = IngestPolicy };
            var report = EnableWebLoader
                ? await Task.Run(() => _pipeline.IngestWebAsync(ds, progress, _ingestCts.Token, ingestOptions), _ingestCts.Token)
                : await Task.Run(() => _pipeline.IngestDirectoryAsync(ds, IngestPath, progress, _ingestCts.Token, ingestOptions), _ingestCts.Token);

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
            if (!IngestDryRun)
            {
                var errorCount = report.Documents.Count(d => d.Status == Hermaeus.Rag.Models.DocumentIngestStatus.Error);
                _ = _activity?.RecordAsync("rag.ingest", ds.Id,
                    errorCount > 0 ? ActivityOutcome.Partial : ActivityOutcome.Succeeded,
                    $"Ingest into {ds.Name}", errorCount > 0 ? $"{errorCount} file(s) errored" : string.Empty, ds.ProjectId);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ingest cancelled.";
            _toasts.Show("RAG ingest cancelled", "Ingest was cancelled before completion.", ToastKind.Info, 5000);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                "RAG ingest cancelled."));
            _ = _activity?.RecordAsync("rag.ingest", string.Empty, ActivityOutcome.Cancelled, "Ingest cancelled");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"RAG ingest failed: {ex.Message}"));
            _toasts.Show("RAG ingest failed", ex.Message, ToastKind.Error, 7000);
            _ = _activity?.RecordAsync("rag.ingest", string.Empty, ActivityOutcome.Failed, "Ingest failed", ex.Message);
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

    /// <summary>doc 03 3.1/1.5: quick-add a watched folder using the same folder
    /// picker/validation as everywhere else a root is chosen.</summary>
    [RelayCommand]
    private async Task AddWatchedSourceAsync(RagDatasetManagerItemViewModel? item)
    {
        if (item?.Dataset is null || RequestWatchedFolderPicker is null) return;
        var picked = await RequestWatchedFolderPicker();
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (!PathRootValidator.TryValidate(picked, out var root, out var error))
        {
            SetError($"Cannot watch that folder: {error}");
            return;
        }

        item.Dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = root });
        await _query.SaveDatasetAsync(item.Dataset);
        await RefreshDatasetManagerAsync();
    }

    [RelayCommand]
    private async Task RemoveWatchedSourceAsync((RagDatasetManagerItemViewModel Item, RagWatchedSource Source) args)
    {
        if (args.Item?.Dataset is null) return;
        args.Item.Dataset.Config.WatchedSources.Remove(args.Source);
        await _query.SaveDatasetAsync(args.Item.Dataset);
        await RefreshDatasetManagerAsync();
    }

    /// <summary>doc 03 3.2: walks watched roots and classifies drift. Changes nothing.</summary>
    [RelayCommand]
    private async Task ScanWatchedSourcesAsync(RagDatasetManagerItemViewModel? item)
    {
        if (item?.Dataset is null || _watchedSources is null || item.WatchedSourceCount == 0) return;

        try
        {
            item.DriftPlan = await _watchedSources.ScanAsync(item.Dataset, _ingestCts?.Token ?? CancellationToken.None);
            // RagDatasetManagerItemViewModel is a plain, non-reactive class
            // (matching MissingFiles/StaleFiles's existing pattern) - a
            // same-reference re-set still raises CollectionChanged.Replace,
            // which is enough to make the ItemsControl re-bind this row.
            var index = DatasetManagerItems.IndexOf(item);
            if (index >= 0) DatasetManagerItems[index] = item;
        }
        catch (Exception ex)
        {
            SetError($"Watched-source scan failed: {ex.Message}");
        }
    }

    /// <summary>doc 03 3.3: applies new and changed files only, through the ingest
    /// pipeline, after confirmation. Missing files are never touched here - that is
    /// RemoveMissingSourcesAsync's job, a second, separate confirmation.</summary>
    [RelayCommand]
    private async Task RefreshWatchedSourcesAsync(RagDatasetManagerItemViewModel? item)
    {
        if (item?.Dataset is null || _watchedSources is null) return;
        if (_watchedSources.IsRefreshing(item.Dataset.Id))
        {
            _toasts.Show("Refresh already running", $"A refresh for '{item.Dataset.Name}' is already in progress.", ToastKind.Warning);
            return;
        }

        var plan = item.DriftPlan ?? await _watchedSources.ScanAsync(item.Dataset);
        item.DriftPlan = plan;
        if (!plan.HasDrift)
        {
            _toasts.Show("Up to date", $"'{item.Dataset.Name}' has no drift to refresh.", ToastKind.Info);
            return;
        }

        var confirmed = RequestConfirmWatchedRefresh is null || await RequestConfirmWatchedRefresh(item, plan);
        if (!confirmed) return;

        try
        {
            var report = await _watchedSources.ApplyNewAndChangedAsync(item.Dataset, plan);
            _query.ClearCache(item.Dataset.Id);
            await LoadDatasetsAsync();
            await RefreshDatasetManagerAsync();

            var errorCount = report.Documents.Count(d => d.Status == DocumentIngestStatus.Error);
            _ = _activity?.RecordAsync("rag.watched-refresh", item.Dataset.Id,
                errorCount > 0 ? ActivityOutcome.Partial : ActivityOutcome.Succeeded,
                $"Watched refresh for {item.Dataset.Name}",
                errorCount > 0 ? $"{errorCount} file(s) errored" : string.Empty, item.Dataset.ProjectId);

            _toasts.Show("Watched sources refreshed", $"{plan.NewFiles.Count} new, {plan.ChangedFiles.Count} changed.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            SetError($"Watched-source refresh failed: {ex.Message}");
            _ = _activity?.RecordAsync("rag.watched-refresh", item.Dataset.Id, ActivityOutcome.Failed, $"Watched refresh for {item.Dataset.Name} failed", ex.Message, item.Dataset.ProjectId);
        }
    }

    /// <summary>doc 03 3.4: unattended refresh triggered by the app (on start, or on
    /// an interval). Ingests new and changed files only - it never deletes, under any
    /// configuration - and skips datasets a manual refresh is already running against
    /// or ones whose embedding model has drifted, so it can never bypass that guard.
    /// Per-dataset failures are recorded and swallowed so one bad dataset does not stop
    /// the rest from refreshing.</summary>
    public async Task RunAutomaticWatchedRefreshAsync(CancellationToken ct = default)
    {
        if (_watchedSources is null) return;

        var datasets = await _query.GetDatasetsAsync();
        foreach (var dataset in datasets)
        {
            ct.ThrowIfCancellationRequested();
            if (dataset.Config.WatchedSources.Count == 0) continue;
            if (_watchedSources.IsRefreshing(dataset.Id)) continue;
            var mismatched = !string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel)
                && !string.Equals(dataset.Config.EmbeddingModel, _settings.Settings.Rag.EmbeddingModel, StringComparison.OrdinalIgnoreCase);
            if (mismatched) continue;

            try
            {
                var plan = await _watchedSources.ScanAsync(dataset, ct);
                if (plan.NewFiles.Count == 0 && plan.ChangedFiles.Count == 0) continue;

                var report = await _watchedSources.ApplyNewAndChangedAsync(dataset, plan, ct);
                _query.ClearCache(dataset.Id);

                var errorCount = report.Documents.Count(d => d.Status == DocumentIngestStatus.Error);
                _ = _activity?.RecordAsync("rag.watched-refresh", dataset.Id,
                    errorCount > 0 ? ActivityOutcome.Partial : ActivityOutcome.Succeeded,
                    $"Automatic watched refresh for {dataset.Name}",
                    $"{plan.NewFiles.Count} new, {plan.ChangedFiles.Count} changed" + (errorCount > 0 ? $", {errorCount} errored" : string.Empty),
                    dataset.ProjectId);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _ = _activity?.RecordAsync("rag.watched-refresh", dataset.Id, ActivityOutcome.Failed,
                    $"Automatic watched refresh for {dataset.Name} failed", ex.Message, dataset.ProjectId);
            }
        }

        await LoadDatasetsAsync();
        await RefreshDatasetManagerAsync();
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

            // Same reason as the ingest path above: re-embedding and the BM25
            // rebuild are CPU-bound and would otherwise run on the UI thread.
            var count = await Task.Run(() => _pipeline.ReindexDatasetAsync(workingDataset, progress, _ingestCts.Token), _ingestCts.Token);
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
            try
            {
                await LoadDatasetsAsync();
                await RefreshDatasetManagerAsync();
            }
            finally
            {
                if (restoreServices is not null)
                {
                    var restoreErrors = await restoreServices();
                    foreach (var error in restoreErrors)
                        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                            $"RAG service restore failed: {error}"));
                }
            }
        }
    }

    [RelayCommand]
    private async Task CopyAnswer()
    {
        if (!string.IsNullOrEmpty(AnswerText))
            await CopyTextAsync(AnswerText, "Answer");
    }

    [RelayCommand]
    private async Task CopySource()
    {
        if (SelectedSource is not null)
            await CopyTextAsync(SelectedSource.Content, "Source");
    }

    [RelayCommand]
    private async Task CopySourcePath()
    {
        var path = SelectedSource?.Path;
        if (!string.IsNullOrWhiteSpace(path))
            await CopyTextAsync(path, "Source path");
    }

    private async Task CopyTextAsync(string text, string label)
    {
        if (RequestCopyToClipboard is null)
            return;

        var copied = false;
        try { copied = await RequestCopyToClipboard(text); }
        catch { }
        _toasts.Show(copied ? $"{label} copied" : $"Could not copy {label.ToLowerInvariant()}",
            copied ? $"{label} text copied to the clipboard." : "The clipboard was unavailable.",
            copied ? ToastKind.Success : ToastKind.Warning, 3000);
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "rag.refresh-datasets", Title: "Refresh datasets", Area: "RAG",
            Description: "Reload the list of RAG datasets from disk.",
            Keywords: ["rag", "dataset", "refresh", "reload"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => RefreshDatasetManagerCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "rag.warm-cache", Title: "Warm query cache", Area: "RAG",
            Description: "Pre-warm the selected dataset's query cache.",
            Keywords: ["rag", "cache", "warm"], Shortcut: "",
            CanExecute: () => SelectedDataset is not null,
            DisabledReason: () => "No dataset selected.",
            Execute: () => WarmCacheCommand.ExecuteAsync(null)));
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

    private bool CanQuery()  => !IsQuerying && !IsIngesting
                                && Datasets.Count > 0
                                && !string.IsNullOrWhiteSpace(QuestionText);
    private bool CanCreateBundledHelpDataset() => !IsIngesting && !IsQuerying && !HasDatasets
                                                  && Directory.Exists(BundledHelpDirectory);
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
            var item = new RagDatasetManagerItemViewModel(dataset, _settings.Settings.Rag.EmbeddingModel)
            {
                // r27 2.7: arithmetic over the chunk count and the embedding
                // dimension, so the number is available without loading
                // anything and moves while a corpus is being ingested.
                ScanIndexBytes = RagScanIndex.ByteSizeFor(dataset.ChunkCount, dataset.Config.EmbeddingDimensions),
                ScanIndexBudgetBytes = _query.ScanIndexBudgetBytes
            };
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

            try
            {
                item.GenerationHistory = await _query.GetGenerationHistoryAsync(dataset.Id);
            }
            catch
            {
                item.GenerationHistory = [];
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
                SourceId = chunk.SourceId,
                SourceRevisionId = chunk.SourceRevisionId,
                ContentHash = chunk.ContentHash,
                GenerationId = chunk.GenerationId,
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

    private void SetQueryDatasetIncluded(string datasetId, bool included)
    {
        var option = QueryDatasetOptions.FirstOrDefault(candidate => candidate.Id == datasetId);
        if (option is not null)
            option.IsIncluded = included;
    }

    private void OnQueryDatasetSelectionChanged()
    {
        _settings.Settings.Rag.LastQueryDatasetIds = QueryDatasetOptions
            .Where(option => option.IsIncluded)
            .Select(option => option.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _ = PersistQueryDatasetSelectionAsync();
        OnPropertyChanged(nameof(QueryDatasetSelectionLabel));
        QueryCommand.NotifyCanExecuteChanged();
    }

    private async Task PersistQueryDatasetSelectionAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Rag,
                $"RAG dataset selection could not be persisted: {ex.Message}"));
        }
    }

    private static string ResolveBundledHelpDirectory()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "BundledHelp");
        if (Directory.Exists(direct))
            return direct;

        var packageDocs = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "docs"));
        return Directory.Exists(packageDocs) ? packageDocs : direct;
    }

    partial void OnIsIngestingChanged(bool value)
    {
        IngestCommand.NotifyCanExecuteChanged();
        QueryCommand.NotifyCanExecuteChanged();
        CreateBundledHelpDatasetCommand.NotifyCanExecuteChanged();
    }
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
