using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

/// <summary>
/// llama.cpp build flavour to install (r14 1.1). Auto resolves against the
/// detected hardware; the concrete values pin a specific accelerator backend.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LlamaRuntimeVariant { Auto, Cpu, Cuda, Vulkan }

/// <summary>
/// Data management configuration including storage directories for user data
/// and local AI assets (models, voices, rerankers, venvs).
/// </summary>
public class DataManagementSettings
{
    /// <summary>
    /// Root directory for Hermaeus application data (conversations, tasks, backups).
    /// </summary>
    public string DataRootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// A confirmed destination awaiting the next process start. The active
    /// root remains <see cref="DataRootDirectory"/> until bootstrap moves and
    /// verifies the workspace before any data-backed service is composed.
    /// </summary>
    public string PendingDataRootDirectory { get; set; } = string.Empty;

    /// <summary>Last bootstrap migration outcome, retained as a user-visible receipt.</summary>
    public string DataRootMigrationReceipt { get; set; } = string.Empty;

    /// <summary>Serialized preview evidence retained until bootstrap migration finishes.</summary>
    public string PendingDataRootMigrationPlan { get; set; } = string.Empty;

    /// <summary>
    /// Root directory for local AI assets (models, voices, rerankers, Python venvs).
    /// </summary>
    public string LocalAiAssetsRoot { get; set; } = string.Empty;

    /// <summary>
    /// Preferred llama.cpp build variant to download for install and update
    /// (r14 1.1). Auto prefers CUDA on NVIDIA and Vulkan on any other real GPU,
    /// falls back to another compatible accelerated asset when upstream lacks
    /// the preferred one, and uses CPU when no GPU is detected. An explicit
    /// choice always wins.
    /// </summary>
    public LlamaRuntimeVariant LlamaRuntimeVariant { get; set; } = LlamaRuntimeVariant.Auto;

    /// <summary>
    /// Backend class of the last managed llama.cpp installation. This is kept
    /// separately from the user's Auto preference so an update can preserve a
    /// working backend without turning Auto into a machine-specific setting.
    /// </summary>
    public LlamaRuntimeVariant InstalledLlamaRuntimeVariant { get; set; } = LlamaRuntimeVariant.Auto;
}
