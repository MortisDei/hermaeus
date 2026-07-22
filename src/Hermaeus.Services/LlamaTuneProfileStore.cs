using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Shared find-or-create upsert for <see cref="LlamaTuneProfile"/> rows, keyed by
/// (resolved model path, file size, mtime) so a re-tune is forced whenever the file on
/// disk changes even if the path is unchanged. Lifted out of ServicesViewModel so the
/// Services page and the Models page's per-model Auto tune write into (and read from)
/// the same store instead of maintaining separate copies (r13 02-model-library.md 2.3).
/// </summary>
public static class LlamaTuneProfileStore
{
    public const int MaxProfiles = 200;

    /// <summary>Resolves a configured model path to an existing file: the path itself if it
    /// is a file, or the single .gguf inside it if it is a directory containing exactly one.
    /// Returns empty when neither resolves (mirrors ServerProcessManager.ResolveModel's
    /// directory-with-one-file convenience without requiring that class as a dependency).</summary>
    public static string ResolveExistingModelPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return string.Empty;

        var trimmed = modelPath.Trim();
        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        if (!Directory.Exists(trimmed))
            return string.Empty;

        try
        {
            var models = Directory.EnumerateFiles(trimmed, "*.gguf", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            return models.Length == 1 ? Path.GetFullPath(models[0]) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static LlamaTuneProfile? Find(AppSettings settings, string modelPath)
    {
        var normalized = ResolveExistingModelPath(modelPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var file = new FileInfo(normalized);
        return settings.LlamaTuneProfiles.FirstOrDefault(profile =>
            string.Equals(Path.GetFullPath(profile.ModelPath), normalized, StringComparison.OrdinalIgnoreCase)
            && profile.ModelSizeBytes == file.Length
            && profile.ModelModifiedAtUtc == file.LastWriteTimeUtc);
    }

    /// <summary>Finds-or-creates the profile for <paramref name="modelPath"/> and updates it in
    /// place. GpuLayers/Threads/TotalLayers/LlamaServerVersion come from <paramref name="result"/>
    /// when supplied (a fresh auto-tune); otherwise GpuLayers/Threads fall back to the caller's
    /// current values (a plain config save keeps whatever was last tuned) and TotalLayers/
    /// LlamaServerVersion are left as previously recorded. Returns null without modifying
    /// settings when the model path does not resolve to an existing file.</summary>
    public static LlamaTuneProfile? Upsert(
        AppSettings settings,
        string modelPath,
        int contextSize,
        string extraArgs,
        int currentGpuLayers,
        int currentThreads,
        ServerTuneResult? result = null)
    {
        var normalized = ResolveExistingModelPath(modelPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var file = new FileInfo(normalized);
        var profile = Find(settings, normalized);
        if (profile is null)
        {
            profile = new LlamaTuneProfile();
            settings.LlamaTuneProfiles.Add(profile);
        }

        profile.ModelPath = normalized;
        profile.ModelSizeBytes = file.Length;
        profile.ModelModifiedAtUtc = file.LastWriteTimeUtc;
        profile.GpuLayers = result?.GpuLayers ?? currentGpuLayers;
        profile.TotalLayers = result?.TotalLayers ?? profile.TotalLayers;
        profile.Threads = result?.Threads ?? currentThreads;
        profile.ContextSize = contextSize;
        profile.ExtraArgs = extraArgs;
        profile.LlamaServerVersion = result?.LlamaServerVersion ?? profile.LlamaServerVersion;
        profile.TunedAtUtc = DateTime.UtcNow;
        Prune(settings);
        return profile;
    }

    /// <summary>Pure staleness predicate for "Auto-tune all" (r13 02-model-library.md 2.4):
    /// missing, a size drift, an mtime drift, or (when both sides are known) a llama-server
    /// build drift all count as stale and get re-tuned; anything else is fresh and skipped.</summary>
    public static bool IsStale(LlamaTuneProfile? profile, long modelSizeBytes, DateTime modelModifiedAtUtc, string? currentLlamaServerVersion = null)
    {
        if (profile is null)
            return true;
        if (profile.ModelSizeBytes != modelSizeBytes)
            return true;
        if (profile.ModelModifiedAtUtc != modelModifiedAtUtc)
            return true;
        if (!string.IsNullOrWhiteSpace(currentLlamaServerVersion)
            && !string.IsNullOrWhiteSpace(profile.LlamaServerVersion)
            && !string.Equals(profile.LlamaServerVersion, currentLlamaServerVersion, StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>Drops profiles whose file no longer exists, then trims to <see cref="MaxProfiles"/>
    /// by keeping the most recently tuned entries.</summary>
    public static void Prune(AppSettings settings)
    {
        var profiles = settings.LlamaTuneProfiles;
        profiles.RemoveAll(p => !File.Exists(p.ModelPath));
        if (profiles.Count > MaxProfiles)
        {
            var stale = profiles.OrderByDescending(p => p.TunedAtUtc).Skip(MaxProfiles).ToList();
            foreach (var p in stale)
                profiles.Remove(p);
        }
    }
}
