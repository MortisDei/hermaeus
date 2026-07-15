using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Aether.Agent.Models;
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
    private readonly ModelProfileService _profiles;
    private readonly IConversationMemoryService _conversationMemory;
    private readonly ConversationExportService _exports;
    private readonly ChatTraceService? _chatTraces;
    private readonly EvalEngine _evalEngine;
    private readonly IWorkspaceActivationService? _workspaceActivation;
    private readonly MemoryInjectionService? _memoryInjection;
    private readonly ILessonStore? _lessons;
    private readonly IVoiceOrchestrator? _voice;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _ttsCts;
    private CancellationTokenSource? _contextUsageCts;
    private DateTime _modelsLoadedAtUtc = DateTime.MinValue;
    private readonly SynchronizationContext? _sync;

    public UiBoundCollection<MessageViewModel> Messages        { get; } = [];

    /// <summary>
    /// Windowed view over <see cref="Messages"/> that the chat view actually
    /// renders (r8 03-performance.md 3.4): opening a long conversation only
    /// materializes the most recent <see cref="MessageWindowSize"/> message
    /// controls instead of every message ever sent. Persistence, memory
    /// extraction, and prompt-history truncation all continue to operate on
    /// the full <see cref="Messages"/> list, never this window.
    /// </summary>
    public UiBoundCollection<MessageViewModel> VisibleMessages { get; } = [];
    private const int MessageWindowSize = 100;
    private int _revealedEarlierMessageCount;

    public bool HasEarlierMessages => Messages.Count > MessageWindowSize + _revealedEarlierMessageCount;
    public UiBoundCollection<LlmModel>         AvailableModels { get; } = [];
    public UiBoundCollection<ChatContextAttachment> ContextAttachments { get; } = [];
    public UiBoundCollection<ChatContextPartViewModel> ContextPreviewParts { get; } = [];
    public UiBoundCollection<ChatTraceViewModel> ChatTraces { get; } = [];
    public UiBoundCollection<CompareModelOptionViewModel> CompareModels { get; } = [];
    public UiBoundCollection<ModelCompareResultViewModel> CompareResults { get; } = [];

    [ObservableProperty] private string    _inputText = string.Empty;
    [ObservableProperty] private bool      _isGenerating;
    [ObservableProperty] private LlmModel? _selectedModel;

    /// <summary>
    /// Whether the active chat model's provider sends prompts off this
    /// machine, from the shared <see cref="CompositeLlmService.Providers"/>
    /// registry keyed by <see cref="LlmModel.ProviderTag"/> - one source of
    /// truth shared with the Privacy Audit (r6 01-first-five-minutes.md 1.3).
    /// </summary>
    public bool HasSelectedModel => SelectedModel is not null;

    /// <summary>Drives the "no model configured" empty state (r8 02-onboarding-and-usability.md 2.6).</summary>
    public bool HasNoAvailableModels => AvailableModels.Count == 0;

    public bool IsSelectedModelRemote =>
        SelectedModel is not null
        && CompositeLlmService.Providers.FirstOrDefault(p =>
            string.Equals(p.Tag, SelectedModel.ProviderTag, StringComparison.OrdinalIgnoreCase))?.IsRemote == true;

    public string SelectedModelLocalityLabel => SelectedModel is null ? string.Empty : IsSelectedModelRemote ? "Remote" : "Local";

    [ObservableProperty] private string    _systemPrompt = string.Empty;
    [ObservableProperty] private string    _currentConversationId = string.Empty;
    [ObservableProperty] private string    _conversationTitle = "New Conversation";
    [ObservableProperty] private bool      _showSystemPrompt;
    [ObservableProperty] private double    _temperature = 0.7;
    [ObservableProperty] private double?   _topP;
    [ObservableProperty] private int?      _topK;
    [ObservableProperty] private double?   _minP;
    [ObservableProperty] private double?   _repeatPenalty;
    [ObservableProperty] private double?   _frequencyPenalty;
    [ObservableProperty] private double?   _presencePenalty;
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
    public Action<string>? RequestNavigate { get; set; }

    public ChatViewModel(
        ILlmService llm,
        IConversationStore store,
        IMemoryStore memoryStore,
        ISettingsService settings,
        ITtsService tts,
        ModelProfileService profiles,
        IToastService toasts,
        IConversationMemoryService conversationMemory,
        IRuntimeLogService runtimeLogs,
        ConversationExportService exports,
        ChatTraceService? chatTraces = null,
        EvalEngine? evalEngine = null,
        IWorkspaceActivationService? workspaceActivation = null,
        MemoryInjectionService? memoryInjection = null,
        ILessonStore? lessons = null,
        IVoiceOrchestrator? voice = null)
    {
        _llm = llm; _store = store; _settings = settings; _tts = tts; _profiles = profiles; _toasts = toasts;
        _memoryStore = memoryStore;
        _conversationMemory = conversationMemory;
        _runtimeLogs = runtimeLogs;
        _exports = exports;
        _chatTraces = chatTraces;
        _memoryInjection = memoryInjection;
        _lessons = lessons;
        _voice = voice;
        _evalEngine = evalEngine ?? new EvalEngine(llm);
        _workspaceActivation = workspaceActivation;
        _sync = SynchronizationContext.Current;
        _temperature  = settings.Settings.Llm.Temperature;
        _topP = settings.Settings.Llm.TopP;
        _topK = settings.Settings.Llm.TopK;
        _minP = settings.Settings.Llm.MinP;
        _repeatPenalty = settings.Settings.Llm.RepeatPenalty;
        _frequencyPenalty = settings.Settings.Llm.FrequencyPenalty;
        _presencePenalty = settings.Settings.Llm.PresencePenalty;
        _systemPrompt = settings.Settings.Llm.DefaultSystemPrompt;
        Messages.CollectionChanged += (_, e) =>
        {
            HasMessages = Messages.Count > 0;
            ScheduleContextUsageRefresh();
            if (e.Action == NotifyCollectionChangedAction.Reset)
                _revealedEarlierMessageCount = 0;
            RefreshVisibleMessageWindow();
        };
        ContextAttachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasContextAttachments));
            SendCommand.NotifyCanExecuteChanged();
            ScheduleContextUsageRefresh();
        };
        AvailableModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoAvailableModels));
        RefreshEstimatedContextUsage();
        _ = Task.Run(RefreshMemoryStatusAsync);
        _ = Task.Run(LoadPersistedChatTracesAsync);
    }

    public async Task LoadModelsAsync(bool force = false)
    {
        if (!force && AvailableModels.Count > 0 && DateTime.UtcNow - _modelsLoadedAtUtc < TimeSpan.FromSeconds(30))
            return;

        if (force)
            _llm.InvalidateModelCache();

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
    private void ShowEarlierMessages()
    {
        _revealedEarlierMessageCount += MessageWindowSize;
        RefreshVisibleMessageWindow();
    }

    private void RefreshVisibleMessageWindow()
    {
        var windowSize = MessageWindowSize + _revealedEarlierMessageCount;
        var skip = Math.Max(0, Messages.Count - windowSize);

        VisibleMessages.Clear();
        for (var i = skip; i < Messages.Count; i++)
            VisibleMessages.Add(Messages[i]);

        OnPropertyChanged(nameof(HasEarlierMessages));
    }

    [RelayCommand]
    private void OpenSetupWizardFromEmptyState() => RequestNavigate?.Invoke("wizard");

    [RelayCommand]
    private void OpenServicesFromEmptyState() => RequestNavigate?.Invoke("services");

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

        var model = activation.ResolvePreferredModel(AvailableModels, m => m.Id);
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
        var accumulator = new ChatStreamAccumulator();
        var traceError = string.Empty;
        var autoSpeak = _voice is not null && _settings.Settings.Tts.AutoSpeakChatReplies;
        var streamingSpeech = autoSpeak && _settings.Settings.Tts.StreamingChatSpeech;
        var chunker = streamingSpeech ? new SentenceChunker() : null;
        try
        {
            var (memoryContext, memorySources, injectedMemoryIds) = await BuildMemoryInjectionAsync(text, _cts.Token);
            foreach (var source in memorySources)
                asst.Sources.Add(source);

            var systemPromptTokens = EstimateTokens(SystemPrompt) + EstimateTokens(memoryContext);
            var history = TruncateHistoryToContextWindow(
                Messages.Where(m => !m.IsStreaming).ToList(),
                ResolveContextWindowLimit(),
                systemPromptTokens,
                Math.Max(0, snapshot.EstimatedTokens - snapshot.HistoryTokens - systemPromptTokens));
            if (history.Count > 0 && history[^1].Role == "user")
                history[^1] = history[^1] with { Content = promptText };

            var result = await ChatSendOrchestrator.StreamAsync(
                _llm, selectedModelId, history,
                BuildChatOptions(memoryContext),
                onToken: token =>
                {
                    if (accumulator.TryAppend(token, force: false, out var flushed))
                    {
                        asst.Content += flushed;
                        ScrollToBottom?.Invoke(this, EventArgs.Empty);
                    }

                    if (chunker is not null)
                        foreach (var chunk in chunker.Append(token))
                            SpeakStreamingChunk(chunk);
                },
                onUsage: usage => UpdateContextUsage(usage, "Reported by provider"),
                _cts.Token);

            if (accumulator.TryAppend(string.Empty, force: true, out var remainder))
            {
                asst.Content += remainder;
                ScrollToBottom?.Invoke(this, EventArgs.Empty);
            }

            asst.DurationMs = result.TotalLatencyMs;
            PerformanceLog = result.Cancelled
                ? $"cancelled after {result.TotalLatencyMs} ms"
                : $"first token {result.FirstTokenMs} ms · full {result.TotalLatencyMs} ms · render batches {accumulator.RenderBatches}";
            asst.IsStreaming = false;

            if (result.Cancelled)
            {
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
            else if (result.Error is not null)
            {
                traceError = result.Error;
                asst.Content = $"Error: {result.Error}";
                asst.IsError = true;
            }
            else
            {
                if (injectedMemoryIds.Count > 0)
                {
                    try
                    {
                        asst.Content = await _conversationMemory.ApplyInjectedMemoryMarkersAsync(asst.Content, injectedMemoryIds, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                            $"Applying memory update/forget markers failed: {ex.Message}"));
                    }
                }

                if (result.Usage is null)
                    RefreshEstimatedContextUsage();
                await PersistAsync();
                _ = Task.Run(() => RunConversationMemoryAsync(CurrentConversationId));

                if (chunker is not null)
                    SpeakStreamingChunk(chunker.Flush());
                else if (autoSpeak)
                    SpeakStreamingChunk(asst.Content);
            }

            AddChatTrace(snapshot, selectedModelId, result.Usage, result.FirstTokenMs, result.TotalLatencyMs, traceError);
        }
        finally
        {
            IsGenerating = false; _cts?.Dispose(); _cts = null;
        }
    }

    private void SpeakStreamingChunk(string? chunk)
    {
        if (_voice is null || string.IsNullOrWhiteSpace(chunk))
            return;
        var sanitized = ChatSpeechSanitizer.Sanitize(chunk);
        if (string.IsNullOrWhiteSpace(sanitized))
            return;
        _ = _voice.EnqueueAsync(new VoiceUtterance(sanitized, VoiceChannel.Chat, VoicePriority.Normal));
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
        _voice?.StopChannel(VoiceChannel.Chat);
    }

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

        var sanitized = ChatSpeechSanitizer.Sanitize(text);
        if (_voice is not null)
        {
            _voice.StopChannel(VoiceChannel.Chat);
            await _voice.EnqueueAsync(new VoiceUtterance(sanitized, VoiceChannel.Chat, VoicePriority.Normal));
            return;
        }

        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _ttsCts = new CancellationTokenSource();

        try
        {
            await _tts.SpeakAsync(sanitized, _ttsCts.Token);
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

        var targets = ModelCompareOrchestrator.ResolveTargets(CompareModels, SelectedModel);
        if (targets.Count == 0)
            return;

        var snapshot = BuildContextSnapshot(text, attachments);
        var history = BuildHistory(snapshot.PromptText);
        var caseId = Guid.NewGuid().ToString("n");

        CompareResults.Clear();
        IsComparingModels = true;
        CompareStatus = $"Comparing {targets.Count} model(s)...";
        try
        {
            var runs = await _evalEngine.RunQuickCompareAsync(
                caseId,
                history,
                targets,
                BuildChatOptions());

            foreach (var run in runs)
                CompareResults.Add(ModelCompareOrchestrator.ToResult(run));

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

    private LlmChatOptions BuildChatOptions(string memoryContext = "") => new()
    {
        SystemPrompt = ComposeSystemPrompt(memoryContext),
        Temperature = Temperature,
        TopP = TopP,
        TopK = TopK,
        MinP = MinP,
        RepeatPenalty = RepeatPenalty,
        FrequencyPenalty = FrequencyPenalty,
        PresencePenalty = PresencePenalty
    };

    private string? ComposeSystemPrompt(string memoryContext)
    {
        var combined = string.IsNullOrWhiteSpace(memoryContext) ? SystemPrompt : $"{SystemPrompt}{memoryContext}";
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    /// <summary>
    /// Selects relevant global memories for the user's new message and builds
    /// the markdown block to append to the system prompt, plus the
    /// <see cref="SourceReference"/>s to show in that turn's Sources panel.
    /// Closes the gap r1 flagged: memory injection existed
    /// (<see cref="MemoryInjectionService"/>) but nothing in chat ever called
    /// it (docs/review/03-next-level-roadmap.md Phase 1, "Chat consumes
    /// citations"). Gated by the same Memory.Enabled setting the memory
    /// status line already respects; best-effort (a failure here should never
    /// block sending a chat message). When <see cref="MemorySettings.ConsumeAgentLessonsInChat"/>
    /// is on, also folds in Global-scope agent lessons (docs/review/archived/r4/03-roadmap.md
    /// Phase 4) as a separate, read-only block - they are never added to
    /// <c>InjectedMemoryIds</c>, so a model's [MEMORY_UPDATE]/[MEMORY_FORGET]
    /// marker can never target a lesson.
    /// </summary>
    private async Task<(string ContextText, List<SourceReference> Sources, List<string> InjectedMemoryIds)> BuildMemoryInjectionAsync(string question, CancellationToken ct)
    {
        if (!_settings.Settings.Memory.Enabled)
            return (string.Empty, [], []);

        var contextText = string.Empty;
        var sources = new List<SourceReference>();
        var injectedIds = new List<string>();

        if (_memoryInjection is not null && !string.IsNullOrWhiteSpace(question))
        {
            try
            {
                var candidates = await _memoryStore.SearchAsync(question, ct);
                var selected = candidates.Count == 0
                    ? []
                    : await _memoryInjection.SelectMemoriesForInjectionAsync(candidates, _settings.Settings.Memory.InjectionTokenBudget);
                if (selected.Count > 0)
                {
                    contextText = _memoryInjection.BuildMemoryContext(selected);
                    sources.AddRange(selected.Where(m => m.Source is not null).Select(m => m.Source!));
                    injectedIds.AddRange(selected.Select(m => m.Id));
                    await _memoryStore.MarkRecalledAsync(injectedIds, ct);
                }
            }
            catch (Exception ex)
            {
                _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                    $"Memory injection failed: {ex.Message}"));
            }
        }

        if (_settings.Settings.Memory.ConsumeAgentLessonsInChat)
            contextText += await BuildLessonContextAsync(ct);

        return (contextText, sources, injectedIds);
    }

    /// <summary>
    /// Global-scope agent lessons formatted as their own markdown block, in
    /// the same style as <see cref="MemoryInjectionService.BuildMemoryContext"/>
    /// but deliberately without an editable id tag: lessons keep their own
    /// pin/retire/delete lifecycle in the Agent workbench, and chat never
    /// mutates the lesson store.
    /// </summary>
    private async Task<string> BuildLessonContextAsync(CancellationToken ct)
    {
        if (_lessons is null) return string.Empty;

        try
        {
            var lessons = await _lessons.ListRelevantAsync(null, includeRetired: false, limit: 10, ct);
            if (lessons.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n---\n## 🧠 Learned Behaviors (Agent Lessons)\n");
            foreach (var lesson in lessons)
            {
                sb.Append("- [").Append(lesson.Outcome).Append(", confidence ").Append(lesson.Confidence.ToString("F2"))
                    .Append(", seen ").Append(lesson.EvidenceCount).Append("x] ").Append(lesson.Claim);
                if (!string.IsNullOrWhiteSpace(lesson.Guidance))
                    sb.Append(" -> ").Append(lesson.Guidance);
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                $"Agent lesson injection failed: {ex.Message}"));
            return string.Empty;
        }
    }

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

        var entry = new ChatTraceEntry(
            trace.Id, trace.Timestamp, trace.ModelId, trace.Provider, trace.Runtime, trace.SystemPrompt,
            trace.AttachmentCount, trace.EstimatedTokens, trace.ProviderUsage, trace.FirstTokenMs,
            trace.TotalLatencyMs, trace.ErrorDetails);
        _ = Task.Run(() => _chatTraces?.PersistAsync(entry, CurrentConversationId) ?? Task.CompletedTask);
    }

    private async Task LoadPersistedChatTracesAsync()
    {
        if (_chatTraces is null || ChatTraces.Count > 0)
            return;

        var entries = await _chatTraces.LoadRecentAsync(50);
        var loaded = entries.Select(entry => new ChatTraceViewModel
        {
            Id = entry.Id,
            Timestamp = entry.Timestamp,
            ModelId = entry.ModelId,
            Provider = entry.Provider,
            Runtime = entry.Runtime,
            SystemPrompt = entry.SystemPrompt,
            AttachmentCount = entry.AttachmentCount,
            EstimatedTokens = entry.EstimatedTokens,
            ProviderUsage = entry.ProviderUsage,
            FirstTokenMs = entry.FirstTokenMs,
            TotalLatencyMs = entry.TotalLatencyMs,
            ErrorDetails = entry.ErrorDetails
        }).ToList();

        RunOnUi(() =>
        {
            if (ChatTraces.Count > 0)
                return;
            foreach (var trace in loaded)
                ChatTraces.Add(trace);
        });
    }

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
        var result = ChatContextUsageCalculator.Compute(usage, limit, kind);
        ContextUsageKind = kind;
        ContextUsageLabel = result.Label;
        ContextUsagePercent = result.Percent;
        ContextUsageWarningLevel = result.WarningLevel;
        IsContextUsageCritical = result.IsCritical;
        IsContextUsageWarning = result.IsWarning;
        ContextUsageTooltip = result.Tooltip;
    }

    private int ResolveContextWindowLimit()
    {
        var chatServer = _settings.Settings.ManagedServers
            .FirstOrDefault(s => s.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)
                && !s.EmbeddingsMode);
        return ChatContextUsageCalculator.ResolveContextWindowLimit(
            SelectedModel?.DefaultContextSize,
            chatServer?.ContextSize,
            _settings.Settings.Llm.MaxTokens);
    }

    public static List<ChatMessage> TruncateHistoryToContextWindow(
        IReadOnlyList<MessageViewModel> messages,
        int contextWindow,
        int systemTokens = 0,
        int currentPromptTokens = 0) =>
        ChatContextUsageCalculator.TruncateHistoryToContextWindow(
            messages.Select(m => new ChatMessage(m.Role, m.Content)).ToList(),
            contextWindow,
            systemTokens,
            currentPromptTokens);

    private async Task PersistAsync()
    {
        if (Messages.Count == 0) return;
        if (ConversationTitle == "New Conversation")
        {
            var first = Messages.FirstOrDefault(m => m.IsUser);
            if (first is not null)
                ConversationTitle = ChatConversationBuilder.AutoTitleFrom(first.Content);
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
        string status;
        if (!_settings.Settings.Memory.Enabled)
        {
            status = "Memory off";
        }
        else
        {
            try
            {
                var recent = await _memoryStore.GetRecentAsync(200);
                if (string.IsNullOrWhiteSpace(CurrentConversationId))
                {
                    status = $"Memory on · {recent.Count} recent";
                }
                else
                {
                    var inConversation = recent.Count(m => string.Equals(m.SourceConversationId, CurrentConversationId, StringComparison.Ordinal));
                    status = $"Memory on · {inConversation} in this chat · {recent.Count} recent";
                }
            }
            catch
            {
                status = "Memory on";
            }
        }

        RunOnUi(() => MemoryStatus = status);
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
        if (value?.DefaultTopP is { } topP)
            TopP = topP;
        if (value?.DefaultTopK is { } topK)
            TopK = topK;
        if (value?.DefaultMinP is { } minP)
            MinP = minP;
        if (value?.DefaultRepeatPenalty is { } repeatPenalty)
            RepeatPenalty = repeatPenalty;
        if (value?.DefaultFrequencyPenalty is { } frequencyPenalty)
            FrequencyPenalty = frequencyPenalty;
        if (value?.DefaultPresencePenalty is { } presencePenalty)
            PresencePenalty = presencePenalty;
        SendCommand.NotifyCanExecuteChanged();
        ScheduleContextUsageRefresh();
        OnPropertyChanged(nameof(HasSelectedModel));
        OnPropertyChanged(nameof(IsSelectedModelRemote));
        OnPropertyChanged(nameof(SelectedModelLocalityLabel));
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
                    RunOnUi(RefreshEstimatedContextUsage);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void RunOnUi(Action action)
    {
        if (_sync is null)
            action();
        else
            _sync.Post(_ => action(), null);
    }

    private sealed record ChatContextSnapshot(
        string PromptText,
        int EstimatedTokens,
        IReadOnlyList<ChatContextPartViewModel> Parts,
        int HistoryTokens);
}
