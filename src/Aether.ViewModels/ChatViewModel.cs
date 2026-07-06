using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public sealed class ChatContextPartViewModel
{
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int EstimatedTokens { get; init; }
    public string TokenLabel => $"{EstimatedTokens:N0} tokens";
}

public sealed class ChatTraceViewModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ModelId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public int MemoryItems { get; init; }
    public int AttachmentCount { get; init; }
    public int RagContextItems { get; init; }
    public int EstimatedTokens { get; init; }
    public ChatTokenUsage? ProviderUsage { get; init; }
    public long FirstTokenMs { get; init; }
    public long TotalLatencyMs { get; init; }
    public string ErrorDetails { get; init; } = string.Empty;
    public string Summary => $"{Timestamp:HH:mm:ss} {ModelId} · {TotalLatencyMs} ms · {EstimatedTokens:N0} est tokens";
    public string UsageLabel => ProviderUsage is null
        ? "provider usage unavailable"
        : $"prompt {ProviderUsage.PromptTokens:N0}, completion {ProviderUsage.CompletionTokens:N0}, total {ProviderUsage.TotalTokens:N0}";
}

public partial class CompareModelOptionViewModel : ObservableObject
{
    public CompareModelOptionViewModel(LlmModel model)
    {
        Model = model;
        IsSelected = false;
    }

    public LlmModel Model { get; }
    public string DisplayName => Model.DisplayName;
    [ObservableProperty] private bool _isSelected;
}

public sealed class ModelCompareResultViewModel
{
    public string ModelId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public long FirstTokenMs { get; init; }
    public long TotalLatencyMs { get; init; }
    public ChatTokenUsage? Usage { get; init; }
    public string Error { get; init; } = string.Empty;
    public string LatencyLabel => string.IsNullOrWhiteSpace(Error)
        ? $"first {FirstTokenMs} ms · total {TotalLatencyMs} ms"
        : $"failed after {TotalLatencyMs} ms";
    public string UsageLabel => Usage is null
        ? "usage unavailable"
        : $"prompt {Usage.PromptTokens:N0}, completion {Usage.CompletionTokens:N0}, total {Usage.TotalTokens:N0}";
    public string QualityNotes => string.IsNullOrWhiteSpace(Error)
        ? $"{Answer.Length:N0} chars · {ChatViewModel.EstimateTokens(Answer):N0} estimated answer tokens"
        : Error;
}

public partial class ChatViewModel : ObservableObject
{
    private readonly ILlmService _llm;
    private readonly IConversationStore _store;
    private readonly ISettingsService _settings;
    private readonly ITtsService _tts;
    private readonly IToastService _toasts;
    private readonly IMemoryStore _memoryStore;
    private readonly IRuntimeLogService _runtimeLogs;
    private readonly IModelProfileService _profiles;
    private readonly IConversationMemoryService _conversationMemory;
    private readonly IConversationExportService _exports;
    private readonly ITraceStore? _traceStore;
    private readonly IEvalEngine _evalEngine;
    private readonly IWorkspaceActivationService? _workspaceActivation;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _ttsCts;
    private CancellationTokenSource? _contextUsageCts;
    private DateTime _modelsLoadedAtUtc = DateTime.MinValue;

    public ObservableCollection<MessageViewModel> Messages        { get; } = [];
    public ObservableCollection<LlmModel>         AvailableModels { get; } = [];
    public ObservableCollection<ChatContextAttachment> ContextAttachments { get; } = [];
    public ObservableCollection<ChatContextPartViewModel> ContextPreviewParts { get; } = [];
    public ObservableCollection<ChatTraceViewModel> ChatTraces { get; } = [];
    public ObservableCollection<CompareModelOptionViewModel> CompareModels { get; } = [];
    public ObservableCollection<ModelCompareResultViewModel> CompareResults { get; } = [];

    [ObservableProperty] private string    _inputText = string.Empty;
    [ObservableProperty] private bool      _isGenerating;
    [ObservableProperty] private LlmModel? _selectedModel;
    [ObservableProperty] private string    _systemPrompt = string.Empty;
    [ObservableProperty] private string    _currentConversationId = string.Empty;
    [ObservableProperty] private string    _conversationTitle = "New Conversation";
    [ObservableProperty] private bool      _showSystemPrompt;
    [ObservableProperty] private double    _temperature = 0.7;
    [ObservableProperty] private bool      _hasMessages;
    [ObservableProperty] private string    _performanceLog = string.Empty;
    [ObservableProperty] private string    _attachmentStatus = string.Empty;
    [ObservableProperty] private bool      _isContextDragOver;
    [ObservableProperty] private string    _contextUsageLabel = string.Empty;
    [ObservableProperty] private string    _contextUsageTooltip = string.Empty;
    [ObservableProperty] private double    _contextUsagePercent;
    [ObservableProperty] private string    _contextUsageKind = "Estimated";
    [ObservableProperty] private string    _contextUsageWarningLevel = "None";
    [ObservableProperty] private bool      _isContextUsageWarning;
    [ObservableProperty] private bool      _isContextUsageCritical;
    [ObservableProperty] private string    _memoryStatus = string.Empty;
    [ObservableProperty] private bool      _showContextInspector;
    [ObservableProperty] private bool      _showChatTraces;
    [ObservableProperty] private bool      _showCompareModels;
    [ObservableProperty] private string    _contextPreviewSummary = string.Empty;
    [ObservableProperty] private string    _contextPreviewRaw = string.Empty;
    [ObservableProperty] private ChatTraceViewModel? _selectedChatTrace;
    [ObservableProperty] private bool      _isComparingModels;
    [ObservableProperty] private string    _compareStatus = string.Empty;
    [ObservableProperty] private string    _activeWorkspaceRoot = string.Empty;

    public event EventHandler?        ScrollToBottom;
    public event EventHandler<string>? ConversationSaved;
    public Action<string>?            RequestCopyToClipboard { get; set; }
    public Action?                    RequestContextFilePicker { get; set; }
    public Func<ConversationExportFormat, Task<string?>>? RequestConversationExportPath { get; set; }
    public ISettingsService Settings => _settings;
    public bool HasContextAttachments => ContextAttachments.Count > 0;

    public ChatViewModel(
        ILlmService llm,
        IConversationStore store,
        IMemoryStore memoryStore,
        ISettingsService settings,
        ITtsService tts,
        IModelProfileService profiles,
        IToastService toasts,
        IConversationMemoryService conversationMemory,
        IRuntimeLogService runtimeLogs,
        IConversationExportService exports,
        ITraceStore? traceStore = null,
        IEvalEngine? evalEngine = null,
        IWorkspaceActivationService? workspaceActivation = null)
    {
        _llm = llm; _store = store; _settings = settings; _tts = tts; _profiles = profiles; _toasts = toasts;
        _memoryStore = memoryStore;
        _conversationMemory = conversationMemory;
        _runtimeLogs = runtimeLogs;
        _exports = exports;
        _traceStore = traceStore;
        _evalEngine = evalEngine ?? new EvalEngine(llm);
        _workspaceActivation = workspaceActivation;
        _temperature  = settings.Settings.Llm.Temperature;
        _systemPrompt = settings.Settings.Llm.DefaultSystemPrompt;
        Messages.CollectionChanged += (_, _) =>
        {
            HasMessages = Messages.Count > 0;
            ScheduleContextUsageRefresh();
        };
        ContextAttachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasContextAttachments));
            SendCommand.NotifyCanExecuteChanged();
            ScheduleContextUsageRefresh();
        };
        RefreshEstimatedContextUsage();
        _ = Task.Run(RefreshMemoryStatusAsync);
        _ = Task.Run(LoadPersistedChatTracesAsync);
    }

    public async Task LoadModelsAsync(bool force = false)
    {
        if (!force && AvailableModels.Count > 0 && DateTime.UtcNow - _modelsLoadedAtUtc < TimeSpan.FromSeconds(30))
            return;

        var current = SelectedModel?.Id;
        var models = await _llm.GetModelsAsync();
        _profiles.ApplyProfiles(models);
        AvailableModels.Clear();
        CompareModels.Clear();
        foreach (var m in models.Where(m => m.IsVisible))
        {
            AvailableModels.Add(m);
            CompareModels.Add(new CompareModelOptionViewModel(m));
        }
        if (AvailableModels.Count > 0)
        {
            var def = _settings.Settings.Llm.DefaultModel;
            var next = AvailableModels.FirstOrDefault(m => m.Id == current)
                ?? AvailableModels.FirstOrDefault(m => m.Id == def)
                ?? AvailableModels[0];
            SelectedModel = next;
        }
        else
        {
            SelectedModel = null;
        }
        _modelsLoadedAtUtc = DateTime.UtcNow;
    }

    [RelayCommand]
    public async Task ActivateWorkspaceAsync()
    {
        if (_workspaceActivation is null || string.IsNullOrWhiteSpace(ActiveWorkspaceRoot) || !Directory.Exists(ActiveWorkspaceRoot))
            return;

        var activation = await _workspaceActivation.ActivateAsync(ActiveWorkspaceRoot);
        if (string.IsNullOrWhiteSpace(activation.PreferredModelId))
            return;

        if (AvailableModels.Count == 0)
            await LoadModelsAsync();

        var model = AvailableModels.FirstOrDefault(m => m.Id == activation.PreferredModelId);
        if (model is not null)
            SelectedModel = model;
    }

    public async Task LoadConversationAsync(string id)
    {
        var conv = await _store.GetByIdAsync(id);
        if (conv is null) return;
        CurrentConversationId = conv.Id;
        ConversationTitle     = conv.Title;
        SystemPrompt          = conv.SystemPrompt;
        if (!string.IsNullOrEmpty(conv.ModelId))
            SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == conv.ModelId) ?? SelectedModel;
        Messages.Clear();
        foreach (var msg in conv.Messages)
        {
            var viewModel = new MessageViewModel
            {
                Role = msg.Role,
                Content = msg.Content,
                OriginalContent = msg.OriginalContent,
                IsError = msg.IsError,
                ModelId = msg.ModelId,
                DurationMs = msg.DurationMs
            };

            foreach (var path in msg.AttachedFilePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                viewModel.AttachedFilePaths.Add(path);

            Messages.Add(viewModel);
        }
        ScrollToBottom?.Invoke(this, EventArgs.Empty);
        await RefreshMemoryStatusAsync();
    }

    public void NewConversation()
    {
        CurrentConversationId = string.Empty;
        ConversationTitle     = "New Conversation";
        SystemPrompt          = _settings.Settings.Llm.DefaultSystemPrompt;
        Messages.Clear();
        _ = Task.Run(RefreshMemoryStatusAsync);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        var attachments = ContextAttachments.ToList();
        if ((string.IsNullOrEmpty(text) && !attachments.Any(a => a.IsReady)) || SelectedModel is null) return;

        var snapshot = BuildContextSnapshot(text, attachments);
        var promptText = snapshot.PromptText;
        var displayText = ChatContextAttachment.BuildDisplayMessage(text, attachments);
        UpdateContextUsage(new ChatTokenUsage(snapshot.EstimatedTokens, 0, snapshot.EstimatedTokens), "Estimated");
        
        var userMessage = new MessageViewModel { Role = "user", Content = displayText, OriginalContent = text };
        // Store attachment paths for regeneration; only include ready attachments
        foreach (var attachment in attachments.Where(a => a.IsReady))
            userMessage.AttachedFilePaths.Add(attachment.FullPath);
        Messages.Add(userMessage);
        
        InputText = string.Empty;
        ClearContextAttachments();

        var selectedModelId = SelectedModel.Id;
        var asst = new MessageViewModel
        {
            Role = "assistant",
            Content = "",
            IsStreaming = true,
            ModelId = selectedModelId
        };
        Messages.Add(asst);
        ScrollToBottom?.Invoke(this, EventArgs.Empty);

        IsGenerating = true;
        _cts = new CancellationTokenSource();
        var streamBuffer = new StringBuilder();
        var streamClock = Stopwatch.StartNew();
        var responseClock = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var renderBatches = 0;
        ChatTokenUsage? reportedUsage = null;
        var traceError = string.Empty;
        try
        {
            var history = TruncateHistoryToContextWindow(
                Messages.Where(m => !m.IsStreaming).ToList(),
                ResolveContextWindowLimit(),
                EstimateTokens(SystemPrompt),
                Math.Max(0, snapshot.EstimatedTokens - snapshot.HistoryTokens - EstimateTokens(SystemPrompt)));
            if (history.Count > 0 && history[^1].Role == "user")
                history[^1] = history[^1] with { Content = promptText };

            await foreach (var evt in _llm.StreamChatAsync(
                selectedModelId, history,
                new LlmChatOptions
                {
                    SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
                    Temperature = Temperature
                },
                _cts.Token))
            {
                if (evt.Usage is not null)
                {
                    reportedUsage = evt.Usage;
                    UpdateContextUsage(evt.Usage, "Reported by provider");
                }

                if (!string.IsNullOrEmpty(evt.ContentDelta))
                {
                    firstTokenMs ??= responseClock.ElapsedMilliseconds;
                    AppendStreamToken(asst, evt.ContentDelta, force: false);
                }
            }
            AppendStreamToken(asst, string.Empty, force: true);
            responseClock.Stop();
            asst.DurationMs = responseClock.ElapsedMilliseconds;
            PerformanceLog = $"first token {firstTokenMs ?? 0} ms · full {asst.DurationMs} ms · render batches {renderBatches}";
            asst.IsStreaming = false;
            if (reportedUsage is null)
                RefreshEstimatedContextUsage();
            await PersistAsync();
            _ = Task.Run(() => RunConversationMemoryAsync(CurrentConversationId));
        }
        catch (OperationCanceledException)
        {
            asst.IsStreaming = false;
            if (string.IsNullOrWhiteSpace(asst.Content))
            {
                Messages.Remove(asst);
            }
            else
            {
                asst.IsError = true;
                asst.Content = $"{asst.Content.TrimEnd()}\n\n[Generation stopped before completion.]";
                await PersistAsync();
            }
        }
        catch (Exception ex)
        {
            traceError = ex.Message;
            asst.Content = $"Error: {ex.Message}";
            responseClock.Stop();
            asst.DurationMs = responseClock.ElapsedMilliseconds;
            PerformanceLog = $"error after {asst.DurationMs} ms";
            asst.IsStreaming = false;
            asst.IsError = true;
        }
        finally
        {
            AddChatTrace(snapshot, selectedModelId, reportedUsage, firstTokenMs ?? 0, responseClock.ElapsedMilliseconds, traceError);
            IsGenerating = false; _cts?.Dispose(); _cts = null;
        }

        void AppendStreamToken(MessageViewModel message, string token, bool force)
        {
            streamBuffer.Append(token);
            if (!force && streamClock.ElapsedMilliseconds < 50 && streamBuffer.Length < 256)
                return;

            if (streamBuffer.Length == 0)
                return;

            message.Content += streamBuffer.ToString();
            renderBatches++;
            streamBuffer.Clear();
            streamClock.Restart();
            ScrollToBottom?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void AttachContextFiles() => RequestContextFilePicker?.Invoke();

    [RelayCommand]
    private async Task ExportMarkdownAsync() => await ExportConversationAsync(ConversationExportFormat.Markdown);

    [RelayCommand]
    private async Task ExportJsonAsync() => await ExportConversationAsync(ConversationExportFormat.Json);

    private async Task ExportConversationAsync(ConversationExportFormat format)
    {
        try
        {
            var conversation = await BuildExportConversationAsync();
            var path = RequestConversationExportPath is null
                ? DefaultExportPath(conversation, format)
                : await RequestConversationExportPath(format);
            if (string.IsNullOrWhiteSpace(path))
                return;

            await _exports.ExportAsync(conversation, path, format);
            _toasts.Show("Conversation exported", path, ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            _toasts.Show("Export failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    private async Task<Conversation> BuildExportConversationAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentConversationId)
            && await _store.GetByIdAsync(CurrentConversationId) is { } stored)
            return stored;

        return new Conversation
        {
            Id = string.IsNullOrWhiteSpace(CurrentConversationId) ? Guid.NewGuid().ToString() : CurrentConversationId,
            Title = ConversationTitle,
            ModelId = SelectedModel?.Id ?? string.Empty,
            SystemPrompt = SystemPrompt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages = Messages.Where(m => !m.IsStreaming).Select(m => new Message
            {
                Id = m.Id,
                ConversationId = CurrentConversationId,
                Role = m.Role,
                Content = m.Content,
                OriginalContent = m.OriginalContent,
                CreatedAt = m.CreatedAt,
                IsError = m.IsError,
                ModelId = m.ModelId,
                DurationMs = m.DurationMs,
                AttachedFilePaths = m.AttachedFilePaths.ToList()
            }).ToList()
        };
    }

    private static string DefaultExportPath(Conversation conversation, ConversationExportFormat format)
    {
        var ext = format == ConversationExportFormat.Json ? "json" : "md";
        var safeTitle = string.Join("-", conversation.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
            safeTitle = "conversation";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"aether-{safeTitle}-{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}");
    }

    public async Task AddContextFilesAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var loaded = await ChatContextAttachment.LoadFilesAsync(paths, ct);
        foreach (var item in loaded)
            ContextAttachments.Add(item);

        var ready = loaded.Count(a => a.IsReady);
        var skipped = loaded.Count - ready;
        AttachmentStatus = skipped == 0
            ? $"{ready} file(s) ready for direct chat context."
            : $"{ready} file(s) ready, {skipped} skipped.";
    }

    [RelayCommand]
    private void RemoveContextAttachment(ChatContextAttachment? attachment)
    {
        if (attachment is not null)
            ContextAttachments.Remove(attachment);
        AttachmentStatus = ContextAttachments.Count == 0 ? string.Empty : AttachmentStatus;
    }

    [RelayCommand]
    private void ClearContextAttachments()
    {
        ContextAttachments.Clear();
        AttachmentStatus = string.Empty;
    }

    [RelayCommand]
    private void CopyMessage(MessageViewModel? msg)
    {
        if (msg is not null) RequestCopyToClipboard?.Invoke(msg.Content);
    }

    [RelayCommand]
    private async Task SpeakMessageAsync(MessageViewModel? msg)
    {
        if (msg is null) return;

        var text = msg.Content;
        if (string.IsNullOrWhiteSpace(text))
            text = msg.OriginalContent;

        if (string.IsNullOrWhiteSpace(text))
        {
            _toasts.Show("Nothing to read aloud", "This message does not contain any text for playback.", ToastKind.Info, 5000);
            return;
        }

        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _ttsCts = new CancellationTokenSource();

        try
        {
            await _tts.SpeakAsync(text, _ttsCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Avoid inserting internal TTS errors into chat history; surface via toast and runtime logs instead
            _toasts.Show("TTS error", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (IsGenerating || Messages.Count == 0) return;
        var lastAsst = Messages.LastOrDefault(m => m.IsAssistant);
        if (lastAsst is not null) Messages.Remove(lastAsst);
        var lastUser = Messages.LastOrDefault(m => m.IsUser);
        if (lastUser is null) return;

        // Recover from structured fields rather than parsing the display-only attachment summary.
        var userText = lastUser.OriginalContent ?? lastUser.Content ?? string.Empty;
        var paths = lastUser.AttachedFilePaths.ToList();

        Messages.Remove(lastUser);
        InputText = userText;
        
        if (paths.Count > 0)
        {
            try
            {
                await AddContextFilesAsync(paths);
            }
            catch { }
        }

        await SendAsync();
    }

    [RelayCommand]
    private void ClearChat()
    {
        if (IsGenerating) return;
        Messages.Clear();
        CurrentConversationId = string.Empty;
        ConversationTitle = "New Conversation";
    }

    [RelayCommand]
    private void ToggleSystemPrompt()
    {
        ShowSystemPrompt = !ShowSystemPrompt;
        if (ShowSystemPrompt)
        {
            ShowContextInspector = false;
            ShowChatTraces = false;
            ShowCompareModels = false;
        }
    }

    [RelayCommand]
    private void ToggleContextInspector()
    {
        ShowContextInspector = !ShowContextInspector;
        if (ShowContextInspector)
        {
            ShowChatTraces = false;
            ShowCompareModels = false;
            ShowSystemPrompt = false;
        }
        if (ShowContextInspector)
            RefreshContextInspector();
    }

    [RelayCommand]
    private void ToggleChatTraces()
    {
        ShowChatTraces = !ShowChatTraces;
        if (ShowChatTraces)
        {
            ShowContextInspector = false;
            ShowCompareModels = false;
            ShowSystemPrompt = false;
        }
    }

    [RelayCommand]
    private void ToggleCompareModels()
    {
        ShowCompareModels = !ShowCompareModels;
        if (ShowCompareModels)
        {
            ShowContextInspector = false;
            ShowChatTraces = false;
            ShowSystemPrompt = false;
        }
    }

    [RelayCommand]
    private async Task CompareSelectedModelsAsync()
    {
        var text = InputText.Trim();
        var attachments = ContextAttachments.ToList();
        if ((string.IsNullOrWhiteSpace(text) && !attachments.Any(a => a.IsReady)) || IsGenerating || IsComparingModels)
            return;

        var selected = CompareModels.Where(model => model.IsSelected).Take(4).ToList();
        if (selected.Count == 0 && SelectedModel is not null)
        {
            selected.Add(new CompareModelOptionViewModel(SelectedModel) { IsSelected = true });
        }

        if (selected.Count == 0)
            return;

        var snapshot = BuildContextSnapshot(text, attachments);
        var history = BuildHistory(snapshot.PromptText);
        var caseId = Guid.NewGuid().ToString("n");
        var targets = selected.Select(o => new EvalTarget(o.Model.Id, Label: o.Model.DisplayName)).ToList();

        CompareResults.Clear();
        IsComparingModels = true;
        CompareStatus = $"Comparing {selected.Count} model(s)...";
        try
        {
            var runs = await _evalEngine.RunQuickCompareAsync(
                caseId,
                history,
                targets,
                new LlmChatOptions
                {
                    SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
                    Temperature = Temperature
                });

            foreach (var run in runs)
            {
                var result = run.CaseResults[0];
                CompareResults.Add(new ModelCompareResultViewModel
                {
                    ModelId = run.Target.ModelId,
                    DisplayName = run.Target.Label ?? run.Target.ModelId,
                    Answer = result.Output,
                    FirstTokenMs = result.FirstTokenMs ?? 0,
                    TotalLatencyMs = result.LatencyMs,
                    Usage = result.PromptTokens is { } pt
                        ? new ChatTokenUsage(pt, result.CompletionTokens ?? 0, pt + (result.CompletionTokens ?? 0))
                        : null,
                    Error = result.Error ?? string.Empty
                });
            }

            CompareStatus = $"Compared {CompareResults.Count} model(s).";
        }
        finally
        {
            IsComparingModels = false;
        }
    }

    private bool CanSend() =>
        !IsGenerating
        && SelectedModel is not null
        && (!string.IsNullOrWhiteSpace(InputText) || ContextAttachments.Any(a => a.IsReady));

    public static int EstimateTokens(string text) => ContextPackBuilder.EstimateTokens(text);

    private int EstimateTokensForSend(string promptText)
    {
        var total = EstimateTokens(SystemPrompt) + EstimateTokens(promptText);
        foreach (var message in Messages.Where(m => !m.IsStreaming))
            total += EstimateTokens(message.Content);
        return total;
    }

    private ChatContextSnapshot BuildContextSnapshot(string text, IReadOnlyList<ChatContextAttachment> attachments)
    {
        var promptText = ChatContextAttachment.BuildPrompt(text, attachments);
        var historyTokens = Messages.Where(m => !m.IsStreaming).Sum(m => EstimateTokens(m.Content));

        var contextParts = new List<ContextPart>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
            contextParts.Add(new ContextPart("System", "System prompt", SystemPrompt));
        contextParts.Add(new ContextPart("User", "Draft message", text));
        foreach (var attachment in attachments.Where(a => a.IsReady))
            contextParts.Add(new ContextPart("Attachment", attachment.FullPath, attachment.Content));

        var parts = contextParts.Select(part => new ChatContextPartViewModel
        {
            Kind = part.Kind,
            Title = part.Title,
            Content = part.Content,
            EstimatedTokens = part.EffectiveTokens
        }).ToList();

        var total = EstimateTokens(SystemPrompt) + EstimateTokens(promptText) + historyTokens;
        return new ChatContextSnapshot(promptText, total, parts, historyTokens);
    }

    private List<ChatMessage> BuildHistory(string promptText)
    {
        var history = Messages.Where(m => !m.IsStreaming)
            .Select(m => new ChatMessage(m.Role, m.Content)).ToList();
        history.Add(new ChatMessage("user", promptText));
        return history;
    }

    private void RefreshContextInspector()
    {
        var snapshot = BuildContextSnapshot(InputText.Trim(), ContextAttachments.ToList());
        ContextPreviewParts.Clear();
        foreach (var part in snapshot.Parts)
            ContextPreviewParts.Add(part);

        ContextPreviewSummary = $"Estimated {snapshot.EstimatedTokens:N0} tokens including {snapshot.HistoryTokens:N0} history tokens.";
        ContextPreviewRaw = string.Join("\n\n---\n\n", snapshot.Parts.Select(part => $"[{part.Kind}] {part.Title}\n{part.Content}"));
    }

    private void AddChatTrace(ChatContextSnapshot snapshot, string modelId, ChatTokenUsage? usage, long firstTokenMs, long totalMs, string error)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
        var trace = new ChatTraceViewModel
        {
            ModelId = modelId,
            Provider = model?.Provider ?? _llm.ProviderName,
            Runtime = model?.ProviderTag ?? _llm.ProviderName,
            SystemPrompt = SystemPrompt,
            AttachmentCount = snapshot.Parts.Count(p => p.Kind == "Attachment"),
            EstimatedTokens = snapshot.EstimatedTokens,
            ProviderUsage = usage,
            FirstTokenMs = firstTokenMs,
            TotalLatencyMs = totalMs,
            ErrorDetails = error
        };
        ChatTraces.Insert(0, trace);
        SelectedChatTrace = trace;
        while (ChatTraces.Count > 50)
            ChatTraces.RemoveAt(ChatTraces.Count - 1);

        _ = Task.Run(() => PersistChatTraceAsync(trace));
    }

    private async Task PersistChatTraceAsync(ChatTraceViewModel trace)
    {
        if (_traceStore is null)
            return;

        try
        {
            await _traceStore.AppendAsync(new TraceRecord
            {
                Id = trace.Id,
                Kind = TraceKind.Chat,
                CreatedAt = trace.Timestamp,
                SourceId = CurrentConversationId,
                ModelId = trace.ModelId,
                Operation = "send",
                FirstTokenMs = trace.FirstTokenMs,
                TotalLatencyMs = trace.TotalLatencyMs,
                PromptTokens = trace.ProviderUsage?.PromptTokens ?? 0,
                CompletionTokens = trace.ProviderUsage?.CompletionTokens ?? 0,
                TotalTokens = trace.ProviderUsage?.TotalTokens ?? trace.EstimatedTokens,
                Error = trace.ErrorDetails,
                DetailJson = JsonSerializer.Serialize(new ChatTraceDetail(
                    trace.Provider, trace.Runtime, trace.SystemPrompt, trace.AttachmentCount, trace.EstimatedTokens))
            });
        }
        catch (Exception ex)
        {
            _runtimeLogs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                $"Chat trace persistence failed: {ex.Message}"));
        }
    }

    private async Task LoadPersistedChatTracesAsync()
    {
        if (_traceStore is null)
            return;

        try
        {
            var records = await _traceStore.GetRecentAsync(TraceKind.Chat, 50);
            if (records.Count == 0 || ChatTraces.Count > 0)
                return;

            foreach (var record in records)
            {
                var detail = TryParseChatTraceDetail(record.DetailJson);
                ChatTraces.Add(new ChatTraceViewModel
                {
                    Id = record.Id,
                    Timestamp = record.CreatedAt,
                    ModelId = record.ModelId,
                    Provider = detail?.Provider ?? string.Empty,
                    Runtime = detail?.Runtime ?? string.Empty,
                    SystemPrompt = detail?.SystemPrompt ?? string.Empty,
                    AttachmentCount = detail?.AttachmentCount ?? 0,
                    EstimatedTokens = detail?.EstimatedTokens ?? record.TotalTokens,
                    ProviderUsage = record.PromptTokens > 0 || record.CompletionTokens > 0
                        ? new ChatTokenUsage(record.PromptTokens, record.CompletionTokens, record.TotalTokens)
                        : null,
                    FirstTokenMs = record.FirstTokenMs,
                    TotalLatencyMs = record.TotalLatencyMs,
                    ErrorDetails = record.Error
                });
            }
        }
        catch
        {
            // Trace history is best-effort; the panel simply starts empty.
        }
    }

    private static ChatTraceDetail? TryParseChatTraceDetail(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatTraceDetail>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ChatTraceDetail(string Provider, string Runtime, string SystemPrompt, int AttachmentCount, int EstimatedTokens);

    private void RefreshEstimatedContextUsage()
    {
        _contextUsageCts?.Cancel();
        var total = EstimateTokens(SystemPrompt) + EstimateTokens(InputText);
        foreach (var message in Messages.Where(m => !m.IsStreaming))
            total += EstimateTokens(message.Content);
        foreach (var attachment in ContextAttachments.Where(a => a.IsReady))
            total += EstimateTokens(attachment.Content);

        UpdateContextUsage(new ChatTokenUsage(total, 0, total), "Estimated");
        if (ShowContextInspector)
            RefreshContextInspector();
    }

    private void UpdateContextUsage(ChatTokenUsage usage, string kind)
    {
        var limit = ResolveContextWindowLimit();
        var total = usage.TotalTokens > 0 ? usage.TotalTokens : usage.PromptTokens + usage.CompletionTokens;
        ContextUsageKind = kind;
        ContextUsageLabel = $"{total:N0} / {limit:N0} tokens";
        ContextUsagePercent = limit <= 0 ? 0 : Math.Clamp(total * 100.0 / limit, 0, 999);
        ContextUsageWarningLevel = ContextUsagePercent >= 95 ? "Critical" :
            ContextUsagePercent >= 80 ? "Warning" : "None";
        IsContextUsageCritical = ContextUsageWarningLevel == "Critical";
        IsContextUsageWarning = ContextUsageWarningLevel == "Warning";
        ContextUsageTooltip = kind == "Reported by provider"
            ? $"Reported by provider. Prompt {usage.PromptTokens:N0}, completion {usage.CompletionTokens:N0}, total {total:N0}."
            : $"Estimated locally from visible chat, system prompt, draft input, and ready attachments. About {ContextUsagePercent:F0}% of the selected context window.";
    }

    private int ResolveContextWindowLimit()
    {
        if (SelectedModel?.DefaultContextSize is { } modelLimit && modelLimit > 0)
            return modelLimit;

        var chatServer = _settings.Settings.ManagedServers
            .FirstOrDefault(s => s.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)
                && !s.EmbeddingsMode);
        if (chatServer?.ContextSize is > 0)
            return chatServer.ContextSize;

        return Math.Max(1, _settings.Settings.Llm.MaxTokens);
    }

    public static List<ChatMessage> TruncateHistoryToContextWindow(
        IReadOnlyList<MessageViewModel> messages,
        int contextWindow,
        int systemTokens = 0,
        int currentPromptTokens = 0)
    {
        var reservedResponseTokens = Math.Max(256, contextWindow / 8);
        var budget = Math.Max(256, contextWindow - systemTokens - currentPromptTokens - reservedResponseTokens);
        var selected = new Stack<ChatMessage>();
        var used = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            var tokens = EstimateTokens(message.Content);
            if (selected.Count > 0 && used + tokens > budget)
                break;
            selected.Push(new ChatMessage(message.Role, message.Content));
            used += tokens;
        }

        return selected.ToList();
    }

    private async Task PersistAsync()
    {
        if (Messages.Count == 0) return;
        if (ConversationTitle == "New Conversation")
        {
            var first = Messages.FirstOrDefault(m => m.IsUser);
            if (first is not null)
            {
                var t = first.Content.Replace('\n', ' ').Trim();
                ConversationTitle = t.Length > 60 ? t[..57] + "..." : t;
            }
        }
        if (string.IsNullOrEmpty(CurrentConversationId))
            CurrentConversationId = Guid.NewGuid().ToString();

        var existing = string.IsNullOrEmpty(CurrentConversationId)
            ? null
            : await _store.GetByIdAsync(CurrentConversationId);

        await _store.SaveAsync(new Conversation
        {
            Id = CurrentConversationId,
            Title = ConversationTitle,
            ModelId = SelectedModel?.Id ?? string.Empty,
            SystemPrompt = SystemPrompt,
            Folder = existing?.Folder ?? string.Empty,
            Tags = existing?.Tags ?? [],
            IsPinned = existing?.IsPinned ?? false,
            IsArchived = existing?.IsArchived ?? false,
            Messages = Messages.Where(m => !m.IsStreaming).Select(m => new Message
            {
                Id = m.Id, ConversationId = CurrentConversationId,
                Role = m.Role,
                Content = m.Content,
                OriginalContent = m.OriginalContent,
                IsError = m.IsError,
                ModelId = m.ModelId,
                DurationMs = m.DurationMs,
                AttachedFilePaths = m.AttachedFilePaths
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }).ToList()
        });
        ConversationSaved?.Invoke(this, CurrentConversationId);
        await RefreshMemoryStatusAsync();
    }

    private async Task RunConversationMemoryAsync(string conversationId)
    {
        try
        {
            await _conversationMemory.RunAutoSummaryAsync(conversationId);
            await RefreshMemoryStatusAsync();
        }
        catch (Exception ex)
        {
            _runtimeLogs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                $"Chat auto-summary failed: {ex.Message}"));
        }
    }

    private async Task RefreshMemoryStatusAsync()
    {
        if (!_settings.Settings.Memory.Enabled)
        {
            MemoryStatus = "Memory off";
            return;
        }

        try
        {
            var recent = await _memoryStore.GetRecentAsync(200);
            if (string.IsNullOrWhiteSpace(CurrentConversationId))
            {
                MemoryStatus = $"Memory on · {recent.Count} recent";
                return;
            }

            var inConversation = recent.Count(m => string.Equals(m.SourceConversationId, CurrentConversationId, StringComparison.Ordinal));
            MemoryStatus = $"Memory on · {inConversation} in this chat · {recent.Count} recent";
        }
        catch
        {
            MemoryStatus = "Memory on";
        }
    }

    partial void OnInputTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        ScheduleContextUsageRefresh();
    }
    partial void OnSelectedModelChanged(LlmModel? value)
    {
        if (value?.DefaultTemperature is { } temp)
            Temperature = temp;
        if (value?.DefaultMaxTokens is { } max && max > 0)
            _settings.Settings.Llm.MaxTokens = max;
        SendCommand.NotifyCanExecuteChanged();
        ScheduleContextUsageRefresh();
    }
    partial void OnIsGeneratingChanged(bool value)       => SendCommand.NotifyCanExecuteChanged();

    private void ScheduleContextUsageRefresh()
    {
        _contextUsageCts?.Cancel();
        _contextUsageCts?.Dispose();
        _contextUsageCts = new CancellationTokenSource();
        var token = _contextUsageCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (!token.IsCancellationRequested)
                    RefreshEstimatedContextUsage();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private sealed record ChatContextSnapshot(
        string PromptText,
        int EstimatedTokens,
        IReadOnlyList<ChatContextPartViewModel> Parts,
        int HistoryTokens);
}
