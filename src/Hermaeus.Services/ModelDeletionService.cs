namespace Hermaeus.Services;

public enum CompanionDisableChoice { KeepFiles, RemoveFiles, Cancel }

public sealed record ModelDeletionPlan(string ModelPath, IReadOnlyList<string> Files, string Description, string AssetsRoot);

public static class ModelDeletionService
{
    public static bool TryPlan(string modelPath, string assetsRoot, bool isRunning, out ModelDeletionPlan? plan, out string error)
    {
        plan = null;
        error = string.Empty;
        if (isRunning) { error = "Stop the managed server before deleting this model."; return false; }
        if (!ModelPathSafety.TryResolveFileUnderRoot(assetsRoot, modelPath, out var normalized, out error)) return false;
        if (!File.Exists(normalized) || (File.GetAttributes(normalized) & FileAttributes.Directory) != 0)
        { error = "The selected model is not a regular file."; return false; }
        return TryPlanFiles(normalized, [normalized], assetsRoot, $"Remove the model file {normalized}.", out plan, out error);
    }

    public static bool TryPlanFiles(
        string modelPath,
        IEnumerable<string> files,
        string assetsRoot,
        string description,
        out ModelDeletionPlan? plan,
        out string error)
    {
        plan = null;
        error = string.Empty;
        var normalizedFiles = new List<string>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!ModelPathSafety.TryResolveFileUnderRoot(assetsRoot, file, out var normalized, out error)
                || !File.Exists(normalized)
                || (File.GetAttributes(normalized) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                error = string.IsNullOrEmpty(error) ? "A companion file is no longer a regular file." : error;
                return false;
            }
            normalizedFiles.Add(normalized);
        }

        if (normalizedFiles.Count == 0)
        {
            error = "No existing files were selected for removal.";
            return false;
        }

        plan = new ModelDeletionPlan(Path.GetFullPath(modelPath), normalizedFiles, description, Path.GetFullPath(assetsRoot));
        return true;
    }

    public static IReadOnlyList<string> DeleteExact(ModelDeletionPlan plan)
    {
        var remaining = new List<string>();
        foreach (var file in plan.Files)
        {
            try
            {
                if (!ModelPathSafety.TryResolveFileUnderRoot(plan.AssetsRoot, file, out var normalized, out _)
                    || !File.Exists(normalized)
                    || (File.GetAttributes(normalized) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    remaining.Add(file);
                    continue;
                }
                File.Delete(normalized);
            }
            catch { remaining.Add(file); }
        }
        return remaining;
    }
}
