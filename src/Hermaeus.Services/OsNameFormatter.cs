namespace Hermaeus.Services;

/// <summary>
/// Windows 11 still self-reports as "Microsoft Windows 10.0.NNNNN" in both
/// RuntimeInformation.OSDescription and the registry ProductName value, so
/// neither can be trusted as-is. Build number is the only honest signal
/// (Windows 11 starts at build 22000). Pure and OS-call-free so it is
/// directly unit testable; the caller supplies the description, version,
/// and optional DisplayVersion (r13 01-system-truth.md 1.2).
/// </summary>
public static class OsNameFormatter
{
    public static string Format(string osDescription, Version version, string? displayVersion = null)
    {
        if (!osDescription.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return osDescription;

        var name = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return string.IsNullOrWhiteSpace(displayVersion)
            ? $"{name} (build {version.Build})"
            : $"{name} {displayVersion} (build {version.Build})";
    }
}
