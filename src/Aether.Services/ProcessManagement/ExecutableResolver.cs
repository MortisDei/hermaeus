namespace Aether.Services.ProcessManagement;

/// <summary>
/// Single resolver for turning a configured executable path/name (a bare
/// name, a directory, or a full path) into a real file on disk. On Windows,
/// PATH and directory probes try the bare name plus each PATHEXT extension
/// (VoiceProviderProcessRunner.FindOnPath already did this correctly; every
/// other resolver in Aether.Services probed only the bare name and could
/// never resolve "llama-server" to "llama-server.exe"). r11 1.3: this is the
/// one place that logic lives now, used by ServerProcessManager,
/// DoctorService, TrustService, LocalAiSetupService, and OrphanServerDetector
/// so they can never disagree about whether an executable resolves.
/// </summary>
public static class ExecutableResolver
{
    /// <summary>
    /// The name(s) that count as a match for <paramref name="baseName"/> on
    /// the target platform: itself when non-Windows or already has an
    /// extension, otherwise itself plus each PATHEXT extension.
    /// </summary>
    public static IReadOnlyList<string> CandidateNames(string baseName, bool? isWindows = null, string? pathExt = null)
    {
        var windows = isWindows ?? OperatingSystem.IsWindows();
        if (!windows || Path.HasExtension(baseName))
            return [baseName];

        pathExt ??= Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? [".EXE", ".BAT", ".CMD"]
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);

        var names = new List<string>(extensions.Length + 1) { baseName };
        names.AddRange(extensions.Select(ext => baseName + ext));
        return names;
    }

    /// <summary>Direct probe of <paramref name="baseName"/> (plus PATHEXT variants on Windows) inside a single directory.</summary>
    public static string? ResolveInDirectory(string directory, string baseName, bool? isWindows = null, string? pathExt = null)
    {
        foreach (var name in CandidateNames(baseName, isWindows, pathExt))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Recursive search for files matching <paramref name="baseName"/> (plus PATHEXT variants) under a directory, capped to the first 2 for ambiguity detection.</summary>
    public static IReadOnlyList<string> FindAllInDirectory(string directory, string baseName, SearchOption searchOption, bool? isWindows = null, string? pathExt = null)
    {
        var names = CandidateNames(baseName, isWindows, pathExt);
        return Directory.EnumerateFiles(directory, "*", searchOption)
            .Where(f => names.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
    }

    /// <summary>Searches PATH for <paramref name="baseName"/> (plus PATHEXT variants on Windows). A rooted input is checked directly, never PATH-searched.</summary>
    public static string? FindOnPath(string baseName, bool? isWindows = null, string? pathOverride = null, string? pathExt = null)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return null;

        if (Path.IsPathRooted(baseName))
            return File.Exists(baseName) ? baseName : null;

        var path = pathOverride ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = CandidateNames(baseName, isWindows, pathExt);
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Full resolution used identically by ServerProcessManager (which throws
    /// with a diagnostic message on failure) and DoctorService (which reports
    /// Ready/Error from the same answer): a directory containing exactly one
    /// match to <paramref name="baseName"/>, a direct file, or PATH.
    /// </summary>
    public static ExecutableResolution Resolve(string configuredPath, string baseName, bool? isWindows = null, string? pathOverride = null, string? pathExt = null)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return ExecutableResolution.Failed(ExecutableResolutionFailure.Empty);

        var trimmed = configuredPath.Trim();
        if (Directory.Exists(trimmed))
        {
            var direct = ResolveInDirectory(trimmed, baseName, isWindows, pathExt);
            if (direct is not null) return ExecutableResolution.Ok(direct);

            var matches = FindAllInDirectory(trimmed, baseName, SearchOption.AllDirectories, isWindows, pathExt);
            return matches.Count switch
            {
                1 => ExecutableResolution.Ok(matches[0]),
                0 => ExecutableResolution.Failed(ExecutableResolutionFailure.NoneInDirectory),
                _ => ExecutableResolution.Failed(ExecutableResolutionFailure.Ambiguous)
            };
        }

        if (File.Exists(trimmed))
            return ExecutableResolution.Ok(trimmed);

        if (!LooksLikePath(trimmed))
        {
            var resolved = FindOnPath(trimmed, isWindows, pathOverride, pathExt);
            if (resolved is not null) return ExecutableResolution.Ok(resolved);
            return ExecutableResolution.Failed(ExecutableResolutionFailure.NotOnPath);
        }

        return ExecutableResolution.Failed(ExecutableResolutionFailure.Missing);
    }
}

public enum ExecutableResolutionFailure { Empty, NoneInDirectory, Ambiguous, NotOnPath, Missing }

public sealed record ExecutableResolution(bool Success, string? Path, ExecutableResolutionFailure Failure)
{
    public static ExecutableResolution Ok(string path) => new(true, path, default);
    public static ExecutableResolution Failed(ExecutableResolutionFailure failure) => new(false, null, failure);
}
