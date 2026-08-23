namespace Hermaeus.Services;

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
        plan = new ModelDeletionPlan(normalized, [normalized], $"Remove the model file {normalized}.", Path.GetFullPath(assetsRoot));
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
