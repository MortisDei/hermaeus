namespace Aether.Core.Models;

/// <summary>
/// Configuration for the optional headless local API host (Aether.LocalApi):
/// a loopback-only HTTP surface that lets editors and scripts reuse Aether's
/// models, memory, and RAG without the desktop UI.
/// </summary>
public class LocalApiSettings
{
    /// <summary>
    /// Off by default. The desktop app only launches the Aether.LocalApi
    /// child process when this is explicitly turned on.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Loopback port the host binds to (127.0.0.1 only, never 0.0.0.0).
    /// </summary>
    public int Port { get; set; } = 39300;

    /// <summary>
    /// Secret-store reference for the bearer token every request must present
    /// in the X-Aether-Token header. Empty means no token is configured, in
    /// which case the host refuses every request rather than allowing
    /// unauthenticated local access.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;
}
