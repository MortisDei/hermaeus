using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Rag;
using Aether.Rag.Eval;
using Aether.Rag.Models;
using Aether.Rag.Pipeline;
using Aether.Core.Services;
using Aether.Core.Models;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

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
    private RagDataset? _targetDatasetForIngest;

    public ObservableCollection<RagDataset>       Datasets  { get; } = [];
    public ObservableCollection<RagSourceViewModel> Sources  { get; } = [];
    public ObservableCollection<RagSourceViewModel> VisibleCitationSources { get; } = [];
    public ObservableCollection<RagEvalResultViewModel> EvalResults { get; } = [];
    public ObservableCollection<RagIngestReportItemViewModel> IngestReportItems { get; } = [];
    public ObservableCollection<RagDatasetManagerItemViewModel> DatasetManagerItems { get; } = [];

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
            var sourcesHeaderParsed = false;

            await foreach (var token in _query.StreamQueryAsync(
                SelectedDataset.Id, QuestionText, opts, _cts.Token))
            {
                if (!sourcesHeaderParsed && token.StartsWith("__RAG_SOURCES__"))
                {
                    ParseSources(token);
                    sourcesHeaderParsed = true;
                    continue;
                }
                if (token.StartsWith("__RAG_TRACE__"))
                {
                    ParseTrace(token);
                    continue;
                }
                answerBuilder.Append(token);
                AnswerText = answerBuilder.ToString();
                ScrollToBottom?.Invoke(this, EventArgs.Empty);
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

        IsIngesting = true;
        IsError = false;
        StatusMessage = string.Empty;
        _ingestCts = new CancellationTokenSource();
        
        Func<Task>? restoreServices = null;

        try
        {
            // Suspend competing services if available
            if (_services is not null)
            {
                var suspendedServerIds = await _services.PrepareEmbeddingServerForWorkAsync();
                var xttsWasRunning = _xtts?.IsRunning == true;
                var kokoroWasRunning = _kokoro?.IsRunning == true;
                
                if (_xtts?.IsRunning == true) _xtts.Stop();
                if (_kokoro?.IsRunning == true) _kokoro.Stop();

                restoreServices = async () =>
                {
                    var errors = new List<string>();
                    try { await _services.RestartServersAsync(suspendedServerIds); }
                    catch (Exception ex) { errors.Add($"LLM: {ex.Message}"); }
                    try { if (xttsWasRunning && _xtts is not null) await _xtts.StartAsync(_settings.Settings, CancellationToken.None); }
                    catch (Exception ex) { errors.Add($"XTTS: {ex.Message}"); }
                    try { if (kokoroWasRunning && _kokoro is not null) await _kokoro.StartAsync(_settings.Settings, CancellationToken.None); }
                    catch (Exception ex) { errors.Add($"Kokoro: {ex.Message}"); }
                };
            }

            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG ingest started for dataset {NewDatasetName}"));
            
            var ds = _targetDatasetForIngest ?? new RagDataset();
            
            if (_targetDatasetForIngest == null)
            {
                // Creating a new dataset
                ds.Name = NewDatasetName.Trim();
                ds.Description = EnableWebLoader
                    ? "Ingested from explicitly configured web URLs"
                    : $"Ingested from {IngestPath}";
                ds.Config = new RagDatasetConfig
                {
                    UseParentChild = UseParentChild,
                    EmbeddingModel = _settings.Settings.Rag.EmbeddingModel,
                    EnableWebLoader = EnableWebLoader,
                    WebUrlList = EnableWebLoader ? WebUrlList.Trim() : string.Empty,
                    WebMaxPages = Math.Clamp(WebMaxPages <= 0 ? 5 : WebMaxPages, 1, 20),
                    ExtractionMode = EnableWebLoader
                        ? RagExtractionMode.WebUrl
                        : RagExtractionMode.TextMarkdown
                };
            }
            else
            {
                // Adding to existing dataset - update path and timestamp
                ds.LastIngestPath = EnableWebLoader
                    ? WebUrlList.Trim()
                    : IngestPath;
                ds.LastIngestUtc = DateTime.UtcNow;
            }

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

            IngestReportItems.Clear();
            foreach (var document in report.Documents)
                IngestReportItems.Add(new RagIngestReportItemViewModel(document));

            // Prefer explicit health property on the report instead of sentinel documents.
            if (report.Health is not null)
            {
                var health = report.Health;
                var parts = new List<string> { $"Files: {health.FileCount}" };
                if (health.DuplicateChunkCount > 0) parts.Add($"Duplicate chunks: {health.DuplicateChunkCount}");
                if (health.EmptyChunkCount > 0) parts.Add($"Empty chunks: {health.EmptyChunkCount}");
                if (health.OversizedFileCount > 0) parts.Add($"Oversized files: {health.OversizedFileCount}");
                if (health.UnsupportedFileCount > 0) parts.Add($"Unsupported files: {health.UnsupportedFileCount}");
                if (health.StaleSourceCount > 0) parts.Add($"Stale sources: {health.StaleSourceCount}");
                if (health.Warnings?.Count > 0) parts.Add(string.Join("; ", health.Warnings));
                var summary = string.Join("; ", parts);
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
                try
                {
                    await restoreServices();
                }
                catch (Exception ex)
                {
                    _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                        $"RAG service restore failed: {ex.Message}"));
                }
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
        try
        {
            var run = await _eval.RunAsync(
                SelectedDataset.Id,
                EvalPath,
                fullAnswer,
                new Progress<string>(s => EvalStatus = s),
                CancellationToken.None);

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
        catch (Exception ex)
        {
            EvalStatus = ex.Message;
            _toasts.Show("RAG eval failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            IsEvaluating = false;
        }
    }

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
                var chunks = await _query.GetChunksForDatasetAsync(dataset.Id, includeEmbeddings: false);
                var sources = chunks.GroupBy(c => c.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
                item.SourceCount = sources.Count;
                item.DuplicateSources = chunks
                    .GroupBy(c => $"{c.SourcePath}::{c.ChunkIndex}", StringComparer.OrdinalIgnoreCase)
                    .Count(g => g.Count() > 1);

                foreach (var source in sources)
                {
                    var path = source.Key;
                    if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!File.Exists(path))
                    {
                        item.MissingFiles++;
                        continue;
                    }

                    var sourceModified = source
                        .Select(c => c.SourceModifiedUtc)
                        .Where(x => x.HasValue)
                        .OrderByDescending(x => x!.Value)
                        .FirstOrDefault();
                    if (sourceModified.HasValue && File.GetLastWriteTimeUtc(path) > sourceModified.Value.AddSeconds(1))
                        item.StaleFiles++;
                }
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

    private void ParseSources(string header)
    {
        try
        {
            var chunks = RagStreamProtocol.ParseSources(header);
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
                    Content = chunk.Content
                });
            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource is not null)
                SelectedSource.IsSelected = true;
            RefreshCitationOverflow();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                $"Could not parse RAG source metadata: {ex.Message}"));
        }
    }

    private void ParseTrace(string token)
    {
        try
        {
            var update = RagStreamProtocol.ParseTrace(token);
            LastTraceId = update.Id;
            LastRetrievalLatencyMs = update.RetrievalLatencyMs;
            LastTotalLatencyMs = update.TotalLatencyMs;
            GroundingScore = update.GroundingScore;
            if (update.ExpandedQuery is not null)
                ExpandedQuery = update.ExpandedQuery;
            if (update.QueryVariants is not null)
                QueryVariants = update.QueryVariants;
            if (update.PlannerNotes is not null)
                PlannerNotes = update.PlannerNotes;
            if (update.ContextPackingSummary is not null)
                ContextPackingSummary = update.ContextPackingSummary;
            if (update.Refused is not null)
                TraceRefused = update.Refused.Value;
            if (update.RefusalReason is not null)
                RefusalReason = update.RefusalReason;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                $"Could not parse RAG trace metadata: {ex.Message}"));
        }
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
    partial void OnNewDatasetNameChanged(string value) => IngestCommand.NotifyCanExecuteChanged();
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
