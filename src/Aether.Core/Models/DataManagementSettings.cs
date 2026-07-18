using System.Text.Json.Serialization;

namespace Aether.Core.Models;

/// <summary>
/// llama.cpp build flavour to install (r14 1.1). Auto resolves against the
/// detected hardware; the concrete values pin a specific accelerator backend.
/// Only Windows publishes distinct cuda/vulkan/cpu binaries that Aether
/// selects between; non-Windows platforms keep the default build regardless.
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
    /// Root directory for Aether application data (conversations, tasks, backups).
    /// </summary>
    public string DataRootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Root directory for local AI assets (models, voices, rerankers, Python venvs).
    /// </summary>
    public string LocalAiAssetsRoot { get; set; } = string.Empty;

    /// <summary>
    /// Preferred llama.cpp build variant to download for install and update
    /// (r14 1.1). Auto picks CUDA on NVIDIA, Vulkan on any other real GPU, and
    /// CPU when no GPU is detected. An explicit choice always wins.
    /// </summary>
    public LlamaRuntimeVariant LlamaRuntimeVariant { get; set; } = LlamaRuntimeVariant.Auto;
}
