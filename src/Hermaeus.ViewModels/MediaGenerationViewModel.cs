using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;

namespace Hermaeus.ViewModels;

public sealed partial class MediaGenerationViewModel : ViewModelBase
{
    private readonly IMediaGenerationService _generation;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _operation;
    private long _version;
    private bool _shutdown;

    public MediaGenerationViewModel(IMediaGenerationService generation, ISettingsService settings)
    {
        _generation = generation;
        _settings = settings;
        Outputs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOutputs));
    }

    public MediaGenerationProtocol[] Protocols { get; } = Enum.GetValues<MediaGenerationProtocol>();
    public MediaKind[] Kinds { get; } = Enum.GetValues<MediaKind>();
    public UiBoundCollection<GeneratedMedia> Outputs { get; } = [];
    public bool HasOutputs => Outputs.Count > 0;
    public Func<GeneratedMedia, Task<string?>>? RequestExportPath { get; set; }
    public Func<CancellationToken, Task<string?>>? RequestWorkflow { get; set; }
    public Action? RequestServices { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsComfy), nameof(IsOpenAi))]
    private MediaGenerationProtocol _protocol = MediaGenerationProtocol.ComfyUi;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAudio), nameof(IsVideo))]
    private MediaKind _kind;
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _voice = "alloy";
    [ObservableProperty] private string _size = string.Empty;
    [ObservableProperty] private int _videoSeconds = 4;
    [ObservableProperty] private string _workflowJson = string.Empty;
    [ObservableProperty] private string _status = "Choose a configured provider and generation kind. Results stay in this session until exported.";
    [ObservableProperty] private string _jobId = string.Empty;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(ExportCommand), nameof(ClearCommand), nameof(LoadWorkflowCommand))]
    private bool _isBusy;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelCommand))] private bool _isGenerating;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ExportCommand))] private GeneratedMedia? _selectedOutput;

    public bool IsComfy => Protocol == MediaGenerationProtocol.ComfyUi;
    public bool IsOpenAi => !IsComfy;
    public bool IsAudio => Kind == MediaKind.Audio;
    public bool IsVideo => Kind == MediaKind.Video;
    private bool CanGenerate() => !IsBusy && !_shutdown;
    private bool CanExport() => CanGenerate() && SelectedOutput is not null;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var config = _settings.Settings.Llm;
        if (IsComfy ? !config.ComfyUiEnabled : !config.OpenAiEnabled)
        { Status = "Enable and configure this provider on Services before generating."; return; }
        var request = new MediaGenerationRequest(Protocol, Kind,
            IsComfy ? config.ComfyUiBaseUrl : config.OpenAiBaseUrl, Prompt, Model,
            IsComfy ? config.ComfyUiApiKey : config.OpenAiApiKey, Voice, Size, VideoSeconds, WorkflowJson);
        var version = ++_version;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = IsGenerating = true;
        JobId = string.Empty;
        Status = "Submitting generation";
        try
        {
            var progress = new InlineProgress(value => RunOnUi(() =>
            {
                if (version != _version || operation.IsCancellationRequested || !IsGenerating) return;
                Status = value.Status;
                if (value.JobId.Length > 0) JobId = value.JobId;
            }));
            var result = await _generation.GenerateAsync(request, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_shutdown || version != _version) return;
            Outputs.Clear();
            foreach (var file in result.Files) Outputs.Add(file);
            SelectedOutput = Outputs.FirstOrDefault();
            JobId = result.JobId;
            Status = $"Generated {Outputs.Count} file(s). Export to keep them; audio/video are not played automatically.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { Status = "Client generation cancelled. An accepted server job may still be running; use its job ID in the provider."; }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or HttpRequestException or TimeoutException)
        { Status = ex.Message; }
        catch (Exception)
        { Status = "Media generation failed. Check the configured endpoint and provider job details."; }
        finally
        {
            _operation = null;
            IsGenerating = IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var file = SelectedOutput!;
        var picker = RequestExportPath;
        if (picker is null) { Status = "The save dialog is unavailable."; return; }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var path = await picker(file);
            if (path is null || _shutdown) return;
            await MediaFileService.SaveAsync(path, file, operation.Token);
            Status = "Media exported. Open it explicitly in your preferred viewer or player.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { Status = "Export cancelled."; }
        catch (Exception) { Status = "Export failed. Choose a writable local path with the matching media extension."; }
        finally { _operation = null; IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task LoadWorkflowAsync()
    {
        var picker = RequestWorkflow;
        if (picker is null) { Status = "The workflow picker is unavailable."; return; }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            var value = await picker(operation.Token);
            if (value is not null && !_shutdown) WorkflowJson = value;
        }
        catch (Exception) { Status = "Could not load the workflow. Choose an API-format JSON file no larger than 1 MiB."; }
        finally { _operation = null; IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Clear()
    {
        ++_version;
        SelectedOutput = null;
        Outputs.Clear();
        JobId = string.Empty;
        Status = "Session results cleared. Backend files and exported files are unchanged.";
    }

    [RelayCommand] private void ConfigureServices() => RequestServices?.Invoke();

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        _shutdown = true;
        ++_version;
        _operation?.Cancel();
        foreach (var task in new[] { GenerateCommand.ExecutionTask, ExportCommand.ExecutionTask, LoadWorkflowCommand.ExecutionTask })
            if (task is not null) await task.WaitAsync(ct);
        SelectedOutput = null;
        Outputs.Clear();
    }

    private sealed class InlineProgress(Action<MediaGenerationProgress> report) : IProgress<MediaGenerationProgress>
    { public void Report(MediaGenerationProgress value) => report(value); }
}
