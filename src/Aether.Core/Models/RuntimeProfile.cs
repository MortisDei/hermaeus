namespace Aether.Core.Models;

public enum RuntimeKind
{
    LlamaCpp,
    Ollama,
    OpenAiCompatible
}

public sealed class RuntimeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Runtime";
    public RuntimeKind Kind { get; set; } = RuntimeKind.OpenAiCompatible;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool StartManagedLlamaServer { get; set; }
    public string LinkedServerId { get; set; } = string.Empty;
}
