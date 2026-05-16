using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;
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

    public event EventHandler?        ScrollToBottom;
    public event EventHandler<string>? ConversationSaved;
    public Action<string>?            RequestCopyToClipboard { get; set; }
    public Action?                    RequestContextFilePicker { get; set; }
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
        IRuntimeLogService runtimeLogs)
    {
        _llm = llm; _store = store; _settings = settings; _tts = tts; _profiles = profiles; _toasts = toasts;
        _memoryStore = memoryStore;
        _conversationMemory = conversationMemory;
        _runtimeLogs = runtimeLogs;
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
            var history = Messages.Where(m => !m.IsStreaming)
                .Select(m => new ChatMessage(m.Role, m.Content)).ToList();
            if (history.Count > 0 && history[^1].Role == "user")
                history[^1] = history[^1] with { Content = promptText };

            await foreach (var evt in _llm.StreamChatEventsAsync(
                selectedModelId, history,
                string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
                Temperature, _cts.Token))
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
            if (string.IsNullOrWhiteSpace(asst.Content)) Messages.Remove(asst);
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
        if (msg is null || string.IsNullOrWhiteSpace(msg.Content)) return;

        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _ttsCts = new CancellationTokenSource();

        try
        {
            await _tts.SpeakAsync(msg.Content, _ttsCts.Token);
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
        CompareResults.Clear();
        IsComparingModels = true;
        CompareStatus = $"Comparing {selected.Count} model(s)...";
        try
        {
            foreach (var option in selected)
            {
                var clock = Stopwatch.StartNew();
                long? firstTokenMs = null;
                ChatTokenUsage? usage = null;
                var answer = new StringBuilder();
                var history = BuildHistory(snapshot.PromptText);
                var error = string.Empty;
                try
                {
                    await foreach (var evt in _llm.StreamChatEventsAsync(
                        option.Model.Id,
                        history,
                        string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
                        Temperature))
                    {
                        if (evt.Usage is not null)
                            usage = evt.Usage;
                        if (!string.IsNullOrEmpty(evt.ContentDelta))
                        {
                            firstTokenMs ??= clock.ElapsedMilliseconds;
                            answer.Append(evt.ContentDelta);
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                finally
                {
                    clock.Stop();
                }

                CompareResults.Add(new ModelCompareResultViewModel
                {
                    ModelId = option.Model.Id,
                    DisplayName = option.Model.DisplayName,
                    Answer = answer.ToString(),
                    FirstTokenMs = firstTokenMs ?? 0,
                    TotalLatencyMs = clock.ElapsedMilliseconds,
                    Usage = usage,
                    Error = error
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

    public static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));

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
        var parts = new List<ChatContextPartViewModel>();

        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            parts.Add(new ChatContextPartViewModel
            {
                Kind = "System",
                Title = "System prompt",
                Content = SystemPrompt,
                EstimatedTokens = EstimateTokens(SystemPrompt)
            });
        }

        parts.Add(new ChatContextPartViewModel
        {
            Kind = "User",
            Title = "Draft message",
            Content = text,
            EstimatedTokens = EstimateTokens(text)
        });

        foreach (var attachment in attachments.Where(a => a.IsReady))
        {
            parts.Add(new ChatContextPartViewModel
            {
                Kind = "Attachment",
                Title = attachment.FullPath,
                Content = attachment.Content,
                EstimatedTokens = EstimateTokens(attachment.Content)
            });
        }

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
