using System.Text.Json;

namespace Hermaeus.Services;

/// <summary>
/// r19 2.3: the CUDA companion archive (several hundred MB) was downloaded
/// into every fresh version directory on every llama.cpp update, even though
/// it changes only when llama.cpp bumps its CUDA toolkit version. GitHub's
/// releases API exposes no per-asset hash to key a content check on, so this
/// keys on identity instead: a marker file recorded next to the companion's
/// extracted files, matched by asset name, verified present with matching
/// sizes before ever being trusted.
/// </summary>
internal static class CudaRuntimeReuse
{
    private const string MarkerFileName = "cudart.json";

    public sealed record MarkerFile(string RelativePath, long SizeBytes);
    public sealed record Marker(string AssetName, IReadOnlyList<MarkerFile> Files);

    /// <summary>
    /// Looks for a sibling version directory under <paramref name="parentInstallRoot"/>
    /// carrying a verified marker for <paramref name="companionAssetName"/> and, if
    /// found, copies its files into <paramref name="destinationDir"/>. Returns the
    /// reused sibling directory's name, or null when no valid match exists (the
    /// caller should fall back to downloading).
    /// </summary>
    public static string? TryReuse(string parentInstallRoot, string destinationDir, string companionAssetName)
    {
        if (string.IsNullOrWhiteSpace(companionAssetName) || !Directory.Exists(parentInstallRoot))
            return null;

        var destinationFull = Path.GetFullPath(destinationDir);
        foreach (var sibling in Directory.EnumerateDirectories(parentInstallRoot))
        {
            if (string.Equals(Path.GetFullPath(sibling), destinationFull, StringComparison.OrdinalIgnoreCase))
                continue;

            var marker = ReadMarker(Path.Combine(sibling, MarkerFileName));
            if (marker is null
                || marker.Files.Count == 0
                || !string.Equals(marker.AssetName, companionAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!marker.Files.All(f => VerifyFile(Path.Combine(sibling, f.RelativePath), f.SizeBytes)))
                continue;

            Directory.CreateDirectory(destinationDir);
            foreach (var f in marker.Files)
            {
                var dest = Path.Combine(destinationDir, f.RelativePath);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(Path.Combine(sibling, f.RelativePath), dest, overwrite: true);
            }

            WriteMarker(destinationDir, marker);
            return Path.GetFileName(sibling);
        }

        return null;
    }

    /// <summary>Records the companion's identity and extracted files (absolute paths, all
    /// under <paramref name="versionDir"/>) right after a fresh download+extract, so a later
    /// update can reuse them.</summary>
    public static void WriteMarker(string versionDir, string companionAssetName, IEnumerable<string> extractedAbsoluteFilePaths)
    {
        var files = extractedAbsoluteFilePaths
            .Select(p => new MarkerFile(Path.GetRelativePath(versionDir, p), SafeLength(p)))
            .ToList();
        WriteMarker(versionDir, new Marker(companionAssetName, files));
    }

    private static void WriteMarker(string versionDir, Marker marker)
    {
        try
        {
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, MarkerFileName), JsonSerializer.Serialize(marker));
        }
        catch
        {
            // Best-effort: a missing marker only costs a future re-download, never correctness.
        }
    }

    private static Marker? ReadMarker(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static bool VerifyFile(string path, long expectedSize)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == expectedSize; }
        catch { return false; }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }
}
