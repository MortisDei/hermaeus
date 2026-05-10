using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly ILlmService _llm;
    private readonly IConversationStore _store;
    private readonly ISettingsService _settings;
    private readonly ITtsService _tts;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _ttsCts;

    public ObservableCollection<MessageViewModel> Messages        { get; } = [];
    public ObservableCollection<LlmModel>         AvailableModels { get; } = [];

    [ObservableProperty] private string    _inputText = string.Empty;
    [ObservableProperty] private bool      _isGenerating;
    [ObservableProperty] private LlmModel? _selectedModel;
    [ObservableProperty] private string    _systemPrompt = string.Empty;
    [ObservableProperty] private string    _currentConversationId = string.Empty;
    [ObservableProperty] private string    _conversationTitle = "New Conversation";
    [ObservableProperty] private bool      _showSystemPrompt;
    [ObservableProperty] private double    _temperature = 0.7;
    [ObservableProperty] private bool      _hasMessages;

    public event EventHandler?        ScrollToBottom;
    public event EventHandler<string>? ConversationSaved;
    public Action<string>?            RequestCopyToClipboard { get; set; }

    public ChatViewModel(ILlmService llm, IConversationStore store, ISettingsService settings, ITtsService tts)
    {
        _llm = llm; _store = store; _settings = settings; _tts = tts;
        _temperature  = settings.Settings.Temperature;
        _systemPrompt = settings.Settings.DefaultSystemPrompt;
        Messages.CollectionChanged += (_, _) => HasMessages = Messages.Count > 0;
    }

    public async Task LoadModelsAsync()
    {
        var current = SelectedModel?.Id;
        var models = await _llm.GetModelsAsync();
        AvailableModels.Clear();
        foreach (var m in models) AvailableModels.Add(m);
        if (AvailableModels.Count > 0)
        {
            var def = _settings.Settings.DefaultModel;
            SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == current)
                ?? AvailableModels.FirstOrDefault(m => m.Id == def)
                ?? AvailableModels[0];
        }
        else
        {
            SelectedModel = null;
        }
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
            Messages.Add(new MessageViewModel
            {
                Role = msg.Role,
                Content = msg.Content,
                IsError = msg.IsError,
                ModelId = msg.ModelId,
                DurationMs = msg.DurationMs
            });
        ScrollToBottom?.Invoke(this, EventArgs.Empty);
    }

    public void NewConversation()
    {
        CurrentConversationId = string.Empty;
        ConversationTitle     = "New Conversation";
        SystemPrompt          = _settings.Settings.DefaultSystemPrompt;
        Messages.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || SelectedModel is null) return;

        Messages.Add(new MessageViewModel { Role = "user", Content = text });
        InputText = string.Empty;

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
        try
        {
            var history = Messages.Where(m => !m.IsStreaming)
                .Select(m => new ChatMessage(m.Role, m.Content)).ToList();

            await foreach (var token in _llm.StreamChatAsync(
                selectedModelId, history,
                string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
                Temperature, _cts.Token))
            {
                AppendStreamToken(asst, token, force: false);
            }
            AppendStreamToken(asst, string.Empty, force: true);
            responseClock.Stop();
            asst.DurationMs = responseClock.ElapsedMilliseconds;
            asst.IsStreaming = false;
            await PersistAsync();
        }
        catch (OperationCanceledException)
        {
            asst.IsStreaming = false;
            if (string.IsNullOrWhiteSpace(asst.Content)) Messages.Remove(asst);
        }
        catch (Exception ex)
        {
            asst.Content = $"Error: {ex.Message}";
            responseClock.Stop();
            asst.DurationMs = responseClock.ElapsedMilliseconds;
            asst.IsStreaming = false;
            asst.IsError = true;
        }
        finally { IsGenerating = false; _cts?.Dispose(); _cts = null; }

        void AppendStreamToken(MessageViewModel message, string token, bool force)
        {
            streamBuffer.Append(token);
            if (!force && streamClock.ElapsedMilliseconds < 50 && streamBuffer.Length < 256)
                return;

            if (streamBuffer.Length == 0)
                return;

            message.Content += streamBuffer.ToString();
            streamBuffer.Clear();
            streamClock.Restart();
            ScrollToBottom?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

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
            Messages.Add(new MessageViewModel
            {
                Role = "assistant",
                Content = $"TTS error: {ex.Message}",
                IsError = true
            });
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
        Messages.Remove(lastUser);
        InputText = lastUser.Content;
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
    private void ToggleSystemPrompt() => ShowSystemPrompt = !ShowSystemPrompt;

    private bool CanSend() =>
        !IsGenerating && !string.IsNullOrWhiteSpace(InputText) && SelectedModel is not null;

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

        await _store.SaveAsync(new Conversation
        {
            Id = CurrentConversationId,
            Title = ConversationTitle,
            ModelId = SelectedModel?.Id ?? string.Empty,
            SystemPrompt = SystemPrompt,
            Messages = Messages.Where(m => !m.IsStreaming).Select(m => new Message
            {
                Id = m.Id, ConversationId = CurrentConversationId,
                Role = m.Role,
                Content = m.Content,
                IsError = m.IsError,
                ModelId = m.ModelId,
                DurationMs = m.DurationMs
            }).ToList()
        });
        ConversationSaved?.Invoke(this, CurrentConversationId);
    }

    partial void OnInputTextChanged(string value)        => SendCommand.NotifyCanExecuteChanged();
    partial void OnSelectedModelChanged(LlmModel? value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsGeneratingChanged(bool value)       => SendCommand.NotifyCanExecuteChanged();
}
