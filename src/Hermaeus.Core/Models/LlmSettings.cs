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
    /// Whether the configured OpenAI-compatible endpoint can enforce a
    /// response shape through <c>response_format</c> (r28 doc 01 1.3/1.4).
    ///
    /// Off by default and ignored for <c>api.openai.com</c>, which is always
    /// treated as capable because structured outputs are documented there.
    /// It exists for the servers that support the field and cannot say so:
    /// LM Studio, vLLM, llama.cpp behind a proxy, and anything else pointed
    /// at by <see cref="OpenAiBaseUrl"/>. Without it, Hermaeus refuses a
    /// constraint against an unknown endpoint rather than sending a field the
    /// server may silently drop, which would turn unconstrained output into
    /// something indistinguishable from success.
    ///
    /// This is a declaration, never a probe: the user is asserting what their
    /// own server does. If the assertion is wrong the server rejects the
    /// request, and that rejection is surfaced rather than retried
    /// unconstrained.
    /// </summary>
    public bool OpenAiSupportsStructuredOutputs { get; set; } = false;

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
