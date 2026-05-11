namespace Aether.Core.Models;

public class AppSettings
{
    public string LlamaCppBaseUrl      { get; set; } = "http://localhost:8080";
    public bool   LlamaCppEnabled      { get; set; } = true;
    public string OpenAiBaseUrl        { get; set; } = "https://api.openai.com";
    public string OpenAiApiKey         { get; set; } = string.Empty;
    public bool   OpenAiEnabled        { get; set; } = false;
    public string EmbeddingModel       { get; set; } = "nomic-embed-text";
    public string DefaultModel         { get; set; } = string.Empty;
    public string DefaultSystemPrompt  { get; set; } = string.Empty;
    public double Temperature          { get; set; } = 0.7;
    public int    MaxTokens            { get; set; } = 4096;
    public bool   StreamResponses      { get; set; } = true;
    public string Theme                { get; set; } = "System";
    public bool   CtrlEnterToSend      { get; set; } = false;
    public double FontSize             { get; set; } = 14;
    public string DataRootDirectory    { get; set; } = string.Empty;
    public bool   RagEnabled           { get; set; } = false;
    public string RagServiceUrl        { get; set; } = "http://localhost:8765";
    public bool   TtsEnabled           { get; set; } = true;
    public string TtsServiceUrl        { get; set; } = "http://127.0.0.1:8020";
    public string TtsSpeaker           { get; set; } = string.Empty;
    public string TtsPythonPath        { get; set; } = "";
    public string TtsScriptPath        { get; set; } = "/mnt/f71464b5-ebe7-4493-9d8d-88ba809a738b/GitHub/apocrypha/xtts_api_server.py";
    public string TtsOutputDirectory   { get; set; } = "";
    public string TtsDevice            { get; set; } = "cpu";
    public string TtsModelVersion      { get; set; } = "2.0.3";
    public bool   TtsPreload           { get; set; } = false;
    public string TtsVoiceDirectory    { get; set; } = "";
    public bool   StartMinimized       { get; set; } = false;
    public bool   ShowQuickChat        { get; set; } = false;
    public List<ModelProfile> ModelProfiles { get; set; } = [];
    public List<RuntimeProfile> RuntimeProfiles { get; set; } = [];
    public List<LocalTaskItem> Tasks { get; set; } = [];
    public List<ScheduledAutomation> Automations { get; set; } = [];

    public List<ServerConfig> ManagedServers { get; set; } =
    [
        new ServerConfig
        {
            Name           = "Chat",
            ExecutablePath = "llama-server",
            Port           = 8080,
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
            Port           = 8081,
            ContextSize    = 2048,
            GpuLayers      = 0,
            Threads        = 4,
            EmbeddingsMode = true,
            AutoStart      = false
        }
    ];
}
