namespace Aether.Core.Models;

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
}
