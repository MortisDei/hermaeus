namespace Hermaeus.Core.Models;

/// <summary>
/// Large Language Model (LLM) configuration including provider endpoints,
/// default model selections, and generation parameters.
/// </summary>
public class LlmSettings
{
    /// <summary>
    /// URL endpoint for llama.cpp/llama-server.
    /// </summary>
    public string LlamaCppBaseUrl { get; set; } = "http://localhost:39201";

    /// <summary>
    /// Enable the llama.cpp provider.
    /// </summary>
    public bool LlamaCppEnabled { get; set; } = true;

    /// <summary>
    /// URL endpoint for OpenAI-compatible APIs.
    /// </summary>
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>
    /// API key for OpenAI-compatible endpoints.
    /// </summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Enable the OpenAI-compatible provider.
    /// </summary>
    public bool OpenAiEnabled { get; set; } = false;

    /// <summary>
    /// Default model to use for chat completions.
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Default system prompt for chat completions.
    /// </summary>
    public string DefaultSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Temperature parameter for model generation (0.0 to 2.0).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens to generate per response.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Stream responses as they are generated.
    /// </summary>
    public bool StreamResponses { get; set; } = true;

    /// <summary>Nucleus sampling cutoff (0-1). Provider default when unset.</summary>
    public double? TopP { get; set; }

    /// <summary>Top-k sampling cutoff. Provider default when unset.</summary>
    public int? TopK { get; set; }

    /// <summary>Minimum token probability relative to the most likely token (0-1). Provider default when unset.</summary>
    public double? MinP { get; set; }

    /// <summary>Repetition penalty (llama.cpp/Ollama naming). Provider default when unset.</summary>
    public double? RepeatPenalty { get; set; }

    /// <summary>OpenAI-style frequency penalty. Provider default when unset.</summary>
    public double? FrequencyPenalty { get; set; }

    /// <summary>OpenAI-style presence penalty. Provider default when unset.</summary>
    public double? PresencePenalty { get; set; }
}
