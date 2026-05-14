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

public partial class RagViewModel : ObservableObject
{
    private readonly RagQueryService _query;
    private readonly RagPipeline     _pipeline;
    private readonly RagEvalService  _eval;
    private readonly IToastService   _toasts;
    private readonly IRuntimeLogService _logs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RagDataset>       Datasets  { get; } = [];
    public ObservableCollection<RagSourceViewModel> Sources  { get; } = [];
    public ObservableCollection<RagSourceViewModel> VisibleCitationSources { get; } = [];
    public ObservableCollection<RagEvalResultViewModel> EvalResults { get; } = [];
    public ObservableCollection<RagIngestReportItemViewModel> IngestReportItems { get; } = [];

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
    [ObservableProperty] private string      _ingestStage     = string.Empty;
    [ObservableProperty] private bool        _useParentChild;
    [ObservableProperty] private float       _groundingScore;
    [ObservableProperty] private bool        _hasAnswer;
    [ObservableProperty] private RagSourceViewModel? _selectedSource;
    [ObservableProperty] private bool        _showSourceInspector;
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

    public event EventHandler? ScrollToBottom;
    public Action<string>? RequestCopyToClipboard { get; set; }
    public bool IsLocalIngest => !EnableWebLoader;

    public RagViewModel(RagQueryService query, RagPipeline pipeline, RagEvalService eval, IToastService toasts, IRuntimeLogService logs)
    {
        _query    = query;
        _pipeline = pipeline;
        _eval     = eval;
        _toasts   = toasts;
        _logs     = logs;
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

        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG ingest started for dataset {NewDatasetName}"));
            var ds = new RagDataset
            {
                Name        = NewDatasetName.Trim(),
                Description = EnableWebLoader
                    ? "Ingested from explicitly configured web URLs"
                    : $"Ingested from {IngestPath}",
                Config      = new RagDatasetConfig
                {
                    UseParentChild = UseParentChild,
                    EnableWebLoader = EnableWebLoader,
                    WebUrlList = EnableWebLoader ? WebUrlList.Trim() : string.Empty,
                    WebMaxPages = Math.Clamp(WebMaxPages <= 0 ? 5 : WebMaxPages, 1, 20),
                    ExtractionMode = EnableWebLoader
                        ? RagExtractionMode.WebUrl
                        : RagExtractionMode.TextMarkdown
                }
            };

            var progress = new Progress<IngestProgress>(p =>
            {
                IngestStage = p.Stage;
                IngestDone  = p.Done;
                IngestTotal = p.Total;
                StatusMessage = p.Detail;
            });

            IngestReport report;
            if (EnableWebLoader)
            {
                report = await _pipeline.IngestWebAsync(ds, progress, CancellationToken.None, new IngestOptions { DryRun = IngestDryRun, DuplicatePolicy = IngestPolicy });
            }
            else
            {
                report = await _pipeline.IngestDirectoryAsync(ds, IngestPath, progress, CancellationToken.None, new IngestOptions { DryRun = IngestDryRun, DuplicatePolicy = IngestPolicy });
            }

            IngestReportItems.Clear();
                // prefer explicit health property on the report
                if (report.Health is not null)
                {
                    var health = report.Health;
                    var parts = new List<string> { $"Files: {health.FileCount}" };
                    if (health.DuplicateChunkCount > 0) parts.Add($"Duplicate chunks: {health.DuplicateChunkCount}");
                    if (health.EmptyChunkCount > 0) parts.Add($"Empty chunks: {health.EmptyChunkCount}");
                    if (health.OversizedFileCount > 0) parts.Add($"Oversized files: {health.OversizedFileCount}");
                    if (health.Warnings?.Count > 0) parts.Add(string.Join("; ", health.Warnings));
                    var summary = string.Join("; ", parts);
                    IngestReportItems.Insert(0, new RagIngestReportItemViewModel(new DocumentIngestReport { Path = "__health__", Status = DocumentIngestStatus.ReportOnly, Message = summary }));
                }

            await LoadDatasetsAsync();
            SelectedDataset = Datasets.FirstOrDefault(d => d.Name == ds.Name);
            NewDatasetName  = string.Empty;
            if (EnableWebLoader)
                WebUrlList = string.Empty;
            else
                IngestPath = string.Empty;
            StatusMessage   = "Ingestion complete.";
            _toasts.Show("RAG ingest complete", $"{report.Summary()}", ToastKind.Success);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG ingest complete for dataset {ds.Name}. {report.Summary()}"));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Rag,
                $"RAG ingest failed: {ex.Message}"));
            _toasts.Show("RAG ingest failed", ex.Message, ToastKind.Error, 7000);
        }
        finally { IsIngesting = false; IngestStage = string.Empty; }
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

    private void ParseSources(string header)
    {
        try
        {
            var json = Regex.Match(header, @"__RAG_SOURCES__(.+)__END_SOURCES__").Groups[1].Value;
            var list = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (list is null) return;
            Sources.Clear();
            SelectedSource = null;
            ShowSourceInspector = false;
            foreach (var el in list)
                Sources.Add(new RagSourceViewModel
                {
                    Rank  = el.GetProperty("rank").GetInt32(),
                    Title = el.GetProperty("title").GetString() ?? string.Empty,
                    File  = el.GetProperty("file").GetString()  ?? string.Empty,
                    Path  = el.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty : string.Empty,
                    Score = el.GetProperty("score").GetSingle(),
                    Content = el.TryGetProperty("content", out var content)
                        ? content.GetString() ?? string.Empty
                        : string.Empty
                });
            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource is not null)
                SelectedSource.IsSelected = true;
            RefreshCitationOverflow();
        }
        catch { }
    }

    private void ParseTrace(string token)
    {
        try
        {
            var json = Regex.Match(token, @"__RAG_TRACE__(.+)__END_TRACE__").Groups[1].Value;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            LastTraceId = root.GetProperty("Id").GetString() ?? string.Empty;
            LastRetrievalLatencyMs = root.GetProperty("RetrievalLatencyMs").GetInt64();
            LastTotalLatencyMs = root.GetProperty("TotalLatencyMs").GetInt64();
            GroundingScore = root.GetProperty("GroundingScore").GetSingle();
        }
        catch { }
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
