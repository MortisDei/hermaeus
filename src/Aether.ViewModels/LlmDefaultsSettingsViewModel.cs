using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class LlmDefaultsSettingsViewModel : ObservableObject
{
    private readonly ISecretStore _secrets;

    [ObservableProperty] private string _llamaCppBaseUrl = "http://localhost:39201";
    [ObservableProperty] private bool _llamaCppEnabled = true;
    [ObservableProperty] private string _openAiBaseUrl = "https://api.openai.com";
    [ObservableProperty] private string _openAiApiKey = string.Empty;
    [ObservableProperty] private bool _openAiEnabled;
    [ObservableProperty] private string _defaultSystemPrompt = string.Empty;
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private int _maxTokens = 4096;
    [ObservableProperty] private double? _topP;
    [ObservableProperty] private int? _topK;
    [ObservableProperty] private double? _minP;
    [ObservableProperty] private double? _repeatPenalty;
    [ObservableProperty] private double? _frequencyPenalty;
    [ObservableProperty] private double? _presencePenalty;

    public LlmDefaultsSettingsViewModel(ISecretStore secrets) => _secrets = secrets;

    public void ReloadFrom(AppSettings settings)
    {
        LlamaCppBaseUrl = settings.Llm.LlamaCppBaseUrl;
        LlamaCppEnabled = settings.Llm.LlamaCppEnabled;
        OpenAiBaseUrl = settings.Llm.OpenAiBaseUrl;
        OpenAiApiKey = _secrets.IsReference(settings.Llm.OpenAiApiKey) ? string.Empty : settings.Llm.OpenAiApiKey;
        OpenAiEnabled = settings.Llm.OpenAiEnabled;
        DefaultSystemPrompt = settings.Llm.DefaultSystemPrompt;
        Temperature = settings.Llm.Temperature;
        MaxTokens = settings.Llm.MaxTokens;
        TopP = settings.Llm.TopP;
        TopK = settings.Llm.TopK;
        MinP = settings.Llm.MinP;
        RepeatPenalty = settings.Llm.RepeatPenalty;
        FrequencyPenalty = settings.Llm.FrequencyPenalty;
        PresencePenalty = settings.Llm.PresencePenalty;
    }

    public async Task ApplyToAsync(AppSettings settings)
    {
        settings.Llm.LlamaCppBaseUrl = LlamaCppBaseUrl;
        settings.Llm.LlamaCppEnabled = LlamaCppEnabled;
        settings.Llm.OpenAiBaseUrl = OpenAiBaseUrl;
        if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            settings.Llm.OpenAiApiKey = await _secrets.StoreAsync("openai-api-key", OpenAiApiKey.Trim());
        settings.Llm.OpenAiEnabled = OpenAiEnabled;
        settings.Llm.DefaultSystemPrompt = DefaultSystemPrompt;
        settings.Llm.Temperature = Temperature;
        settings.Llm.MaxTokens = MaxTokens;
        settings.Llm.TopP = TopP;
        settings.Llm.TopK = TopK;
        settings.Llm.MinP = MinP;
        settings.Llm.RepeatPenalty = RepeatPenalty;
        settings.Llm.FrequencyPenalty = FrequencyPenalty;
        settings.Llm.PresencePenalty = PresencePenalty;
    }
}
