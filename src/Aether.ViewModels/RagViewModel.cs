using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Rag;
using Aether.Rag.Models;
using Aether.Rag.Pipeline;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class RagSourceViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public int    Rank    { get; init; }
    public string Title   { get; init; } = string.Empty;
    public string File    { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public float  Score   { get; init; }
    public string ScoreDisplay => $"{Score:F3}";
    public string CitationLabel => $"[{Rank}] {Title}";
    public string Snippet
    {
        get
        {
            var flat = Content.Replace('\n', ' ').Trim();
            return flat.Length > 220 ? flat[..217] + "..." : flat;
        }
    }
}

public partial class RagViewModel : ObservableObject
{
    private readonly RagQueryService _query;
    private readonly RagPipeline     _pipeline;
    private readonly IToastService   _toasts;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RagDataset>       Datasets  { get; } = [];
    public ObservableCollection<RagSourceViewModel> Sources  { get; } = [];

    [ObservableProperty] private RagDataset? _selectedDataset;
    [ObservableProperty] private string      _questionText    = string.Empty;
    [ObservableProperty] private string      _answerText      = string.Empty;
    [ObservableProperty] private bool        _isQuerying;
    [ObservableProperty] private bool        _isIngesting;
    [ObservableProperty] private string      _ingestPath      = string.Empty;
    [ObservableProperty] private string      _newDatasetName  = string.Empty;
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

    public event EventHandler? ScrollToBottom;
    public Action<string>? RequestCopyToClipboard { get; set; }

    public RagViewModel(RagQueryService query, RagPipeline pipeline, IToastService toasts)
    {
        _query    = query;
        _pipeline = pipeline;
        _toasts   = toasts;
    }

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
                answerBuilder.Append(token);
                AnswerText = answerBuilder.ToString();
                ScrollToBottom?.Invoke(this, EventArgs.Empty);
            }

            HasAnswer = !string.IsNullOrWhiteSpace(AnswerText);
            GroundingScore = RagQueryService.GroundingScore(
                AnswerText,
                string.Join(" ", Sources.Select(s => s.Content)));
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
        if (string.IsNullOrWhiteSpace(IngestPath) || string.IsNullOrWhiteSpace(NewDatasetName)) return;

        IsIngesting = true;
        IsError = false;
        StatusMessage = string.Empty;

        try
        {
            var ds = new RagDataset
            {
                Name        = NewDatasetName.Trim(),
                Description = $"Ingested from {IngestPath}",
                Config      = new RagDatasetConfig
                {
                    UseParentChild = UseParentChild
                }
            };

            await _pipeline.IngestDirectoryAsync(
                ds,
                IngestPath,
                new Progress<IngestProgress>(p =>
                {
                    IngestStage = p.Stage;
                    IngestDone  = p.Done;
                    IngestTotal = p.Total;
                    StatusMessage = p.Detail;
                }),
                CancellationToken.None);

            await LoadDatasetsAsync();
            SelectedDataset = Datasets.FirstOrDefault(d => d.Name == ds.Name);
            NewDatasetName  = string.Empty;
            IngestPath      = string.Empty;
            StatusMessage   = "Ingestion complete.";
            _toasts.Show("RAG ingest complete", $"Dataset \"{ds.Name}\" is ready.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
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

    private bool CanQuery()  => !IsQuerying && !IsIngesting && SelectedDataset is not null
                                && !string.IsNullOrWhiteSpace(QuestionText);
    private bool CanIngest() => !IsIngesting && !IsQuerying
                                && !string.IsNullOrWhiteSpace(IngestPath)
                                && !string.IsNullOrWhiteSpace(NewDatasetName);

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
                    Score = el.GetProperty("score").GetSingle(),
                    Content = el.TryGetProperty("content", out var content)
                        ? content.GetString() ?? string.Empty
                        : string.Empty
                });
            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource is not null)
                SelectedSource.IsSelected = true;
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

    partial void OnQuestionTextChanged(string value) => QueryCommand.NotifyCanExecuteChanged();
    partial void OnSelectedDatasetChanged(RagDataset? value) => QueryCommand.NotifyCanExecuteChanged();
    partial void OnIsQueryingChanged(bool value) => QueryCommand.NotifyCanExecuteChanged();
    partial void OnIngestPathChanged(string value) => IngestCommand.NotifyCanExecuteChanged();
    partial void OnNewDatasetNameChanged(string value) => IngestCommand.NotifyCanExecuteChanged();
}
