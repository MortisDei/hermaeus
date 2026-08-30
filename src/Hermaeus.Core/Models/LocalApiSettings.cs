namespace Hermaeus.Core.Models;

/// <summary>
/// One named bearer token calling apps can present in the X-Hermaeus-Token
/// header. Replaces a single shared token (docs/review/03-next-level-roadmap.md
/// Phase 2) so each calling app gets its own credential: the token identifies
/// who is calling (Privacy Audit can show verified per-app activity, not just
/// a self-reported name) and a compromised or retired integration can be
/// revoked individually without breaking every other caller.
/// </summary>
public sealed class LocalApiTokenEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Human-readable label shown in Settings and Privacy Audit.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Secret-store reference for the actual bearer token value.</summary>
    public string SecretRef { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Versioned, deny-by-default Agent authority proposed for this token.
    /// The R31 Local API process does not expose Agent execution routes because
    /// it is not the single owner of Desktop's task state, so this scope is
    /// currently inert and retained as the reviewed future contract.
    /// </summary>
    public LocalApiAgentScope AgentScope { get; set; } = new();
}

public enum LocalApiAgentOperation
{
    CreateTask,
    StartTask,
    ReadTask,
    ReadRun,
    SteerTask,
    ContinueTask,
    ReadOutput,
    ReadDecisions
}

/// <summary>
/// Explicit Agent authority for one named Local API token. Deserializing an
/// older token produces this disabled empty scope, so a settings migration can
/// never grant Agent access. An empty model allowlist means any currently
/// visible model; workspace and optional project allowlists are always exact.
/// </summary>
public sealed class LocalApiAgentScope
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool Enabled { get; set; }
    public List<LocalApiAgentOperation> AllowedOperations { get; set; } = [];
    public List<string> AllowedWorkspaceProfileIds { get; set; } = [];
    public List<string> AllowedModelIds { get; set; } = [];
    public List<string> AllowedProjectIds { get; set; } = [];
    public bool AllowReadOtherOwnedTasks { get; set; }
    public int MaxConcurrentRuns { get; set; } = 1;
}

/// <summary>
/// Configuration for the optional headless local API host (Hermaeus.LocalApi):
/// a loopback-only HTTP surface that lets editors and scripts reuse Hermaeus's
/// models, memory, and RAG without the desktop UI.
/// </summary>
public class LocalApiSettings
{
    /// <summary>
    /// Off by default. The desktop app only launches the Hermaeus.LocalApi
    /// child process when this is explicitly turned on.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Loopback port the host binds to (127.0.0.1 only, never 0.0.0.0).
    /// </summary>
    public int Port { get; set; } = 39300;

    /// <summary>
    /// Named bearer tokens; a request authenticates by matching the
    /// X-Hermaeus-Token header against any entry's resolved secret. Empty means
    /// no token is configured, in which case the host refuses every request
    /// rather than allowing unauthenticated local access.
    /// </summary>
    public List<LocalApiTokenEntry> Tokens { get; set; } = [];

    /// <summary>
    /// Legacy single shared-token field from before per-app tokens existed.
    /// Read only once, by <c>SettingsService</c>'s load-time migration, which
    /// converts it into a "Default" entry in <see cref="Tokens"/> and clears
    /// it; nothing else should read or write this field.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;
}
