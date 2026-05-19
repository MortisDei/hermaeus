namespace Aether.Core.Models;

public class AppSettings
{
    /// <summary>
    /// LLM provider configuration (llama.cpp, OpenAI, model selection, generation parameters).
    /// </summary>
    public LlmSettings Llm { get; set; } = new();

    /// <summary>
    /// Text-to-Speech provider configuration (voice, device, model paths).
    /// </summary>
    public TtsSettings Tts { get; set; } = new();

    /// <summary>
    /// Retrieval-Augmented Generation configuration (service URL, reranking, embeddings).
    /// </summary>
    public RagSettings Rag { get; set; } = new();

    /// <summary>
    /// User interface configuration (theme, hotkeys, tray, fonts).
    /// </summary>
    public UiSettings Ui { get; set; } = new();

    /// <summary>
    /// Data management configuration (storage directories for data and AI assets).
    /// </summary>
    public DataManagementSettings DataManagement { get; set; } = new();

    /// <summary>
    /// Whether the first-run setup wizard has been completed.
    /// </summary>
    public bool SetupWizardCompleted { get; set; } = false;

    /// <summary>
    /// Voice provider-specific configurations (per-provider settings).
    /// </summary>
    public Dictionary<string, VoiceProviderConfig> VoiceProviderConfigs { get; set; } = [];

    /// <summary>
    /// Saved chat model profiles with display names, tags, and defaults.
    /// </summary>
    public List<ModelProfile> ModelProfiles { get; set; } = [];

    /// <summary>
    /// Verified llama.cpp launch settings keyed to local GGUF model files.
    /// </summary>
    public List<LlamaTuneProfile> LlamaTuneProfiles { get; set; } = [];

    /// <summary>
    /// Alternative LLM runtime profiles (llama.cpp, Ollama, remote endpoints).
    /// </summary>
    public List<RuntimeProfile> RuntimeProfiles { get; set; } = [];

    /// <summary>
    /// Local task items for task automation and scheduling.
    /// </summary>
    public List<LocalTaskItem> Tasks { get; set; } = [];

    /// <summary>
    /// Scheduled automations for periodic task execution.
    /// </summary>
    public List<ScheduledAutomation> Automations { get; set; } = [];

    /// <summary>
    /// Managed llama-server instances (Chat, Embeddings) with port, thread, and GPU configuration.
    /// </summary>
    public List<ServerConfig> ManagedServers { get; set; } =
    [
        new ServerConfig
        {
            Name           = "Chat",
            ExecutablePath = "llama-server",
            Port           = 39201,
            ContextSize    = 4096,
            GpuLayers      = 0,
            Threads        = 4,
            EmbeddingsMode = false,
            AutoStart      = false
        },
        new ServerConfig
        {
            Name           = "Embeddings",
            ExecutablePath = "llama-server",
            Port           = 39202,
            ContextSize    = 2048,
            GpuLayers      = 0,
            Threads        = 4,
            EmbeddingsMode = true,
            AutoStart      = false
        }
    ];

    /// <summary>
    /// Chat memory feature configuration (storage, injection, retention, encryption).
    /// </summary>
    public MemorySettings Memory { get; set; } = new();
}
