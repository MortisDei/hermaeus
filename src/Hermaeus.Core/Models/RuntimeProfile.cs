namespace Hermaeus.Core.Models;

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

    /// <summary>
    /// Only meaningful when <see cref="Kind"/> is <see cref="RuntimeKind.LlamaCpp"/>;
    /// <see cref="RuntimeProfileService.NormalizeProfile"/> forces this to false for
    /// every other kind so a future runtime can never inherit a stray llama.cpp-only
    /// setting.
    /// </summary>
    public bool StartManagedLlamaServer { get; set; }

    /// <summary>
    /// The <see cref="ServerConfig"/> to start when <see cref="StartManagedLlamaServer"/>
    /// is set. Only meaningful for <see cref="RuntimeKind.LlamaCpp"/>; see
    /// <see cref="StartManagedLlamaServer"/>.
    /// </summary>
    public string LinkedServerId { get; set; } = string.Empty;
}
