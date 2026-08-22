using System.Formats.Tar;
using System.IO.Compression;

namespace Hermaeus.Services;

/// <summary>
/// Extracts .zip and .tar.gz archives into a destination directory with a
/// zip-slip guard: every entry's resolved destination must stay inside that
/// directory, or the entry is rejected outright (r11 1.1/1.2). Both
/// System.IO.Compression and System.Formats.Tar are BCL, so this adds no new
/// NuGet dependency.
/// </summary>
public static class ArchiveExtractor
{
    public static async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destinationDirectory));

        if (IsTarGz(archivePath))
            await ExtractTarGzAsync(archivePath, destinationRoot, ct);
        else
            ExtractZip(archivePath, destinationRoot);
    }

    public static bool IsTarGz(string archivePath) =>
        archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static void ExtractZip(string archivePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destPath = ResolveEntryPath(destinationRoot, entry.FullName);

            // A directory entry has an empty file name (trailing '/' in FullName).
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destinationRoot, CancellationToken ct)
    {
        var deferredLinks = new List<(string LinkPath, string TargetPath)>();
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzip);

        while (await tarReader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
        {
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                if (string.IsNullOrWhiteSpace(entry.LinkName) || Path.IsPathFullyQualified(entry.LinkName))
                    throw new InvalidOperationException($"Archive link '{entry.Name}' has an invalid target.");

                var linkPath = ResolveEntryPath(destinationRoot, entry.Name);
                var targetRelativePath = entry.EntryType == TarEntryType.SymbolicLink
                    ? Path.Combine(Path.GetDirectoryName(entry.Name) ?? string.Empty, entry.LinkName)
                    : entry.LinkName;
                var targetPath = ResolveEntryPath(destinationRoot, targetRelativePath);
                deferredLinks.Add((linkPath, targetPath));
                continue;
            }

            var destPath = ResolveEntryPath(destinationRoot, entry.Name);

            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await using var outStream = File.Create(destPath);
            if (entry.DataStream is not null)
                await entry.DataStream.CopyToAsync(outStream, ct);
        }

        // Release archives use relative symlinks for ELF SONAMEs, for example
        // libllama-common.so.0 -> libllama-common.so.0.0.10034. Materialise
        // those links as regular files after extraction. This keeps the
        // installed package runnable without creating filesystem links from
        // archive-controlled data. Both link and target were already proven
        // to stay under destinationRoot above.
        var unresolved = deferredLinks;
        while (unresolved.Count > 0)
        {
            var next = new List<(string LinkPath, string TargetPath)>();
            var copied = 0;
            foreach (var link in unresolved)
            {
                if (!File.Exists(link.TargetPath))
                {
                    next.Add(link);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(link.LinkPath)!);
                File.Copy(link.TargetPath, link.LinkPath, overwrite: true);
                copied++;
            }

            if (next.Count == 0)
                break;
            if (copied == 0)
                throw new InvalidOperationException($"Archive link target was not extracted: {next[0].TargetPath}");
            unresolved = next;
        }
    }

    /// <summary>Resolves an archive-relative entry path against the destination root and rejects anything that would escape it (zip-slip).</summary>
    private static string ResolveEntryPath(string destinationRoot, string entryRelativePath)
    {
        var normalizedRelative = entryRelativePath.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(destinationRoot, normalizedRelative));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(destinationRoot, comparison))
            throw new InvalidOperationException($"Archive entry '{entryRelativePath}' would extract outside the target directory.");

        return combined;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
