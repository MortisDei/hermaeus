namespace Hermaeus.Core.Models;

/// <summary>
/// Result of a llama.cpp update (r14 3.2/3.3): the new executable path, the
/// install root, and the superseded version directories the update flow may
/// offer to prune. <see cref="PrunableVersionDirectories"/> is empty on failure.
/// </summary>
public sealed record LlamaUpdateOutcome(
    bool Success,
    string? UpdatedPath,
    string? InstallRoot,
    IReadOnlyList<string> PrunableVersionDirectories,
    string Log)
{
    public static LlamaUpdateOutcome Ok(string updatedPath, string installRoot, IReadOnlyList<string> prunable)
        => new(true, updatedPath, installRoot, prunable, string.Empty);

    public static LlamaUpdateOutcome Failed(string log)
        => new(false, null, null, [], log);
}
