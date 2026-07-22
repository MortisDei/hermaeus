namespace Hermaeus.Core.Services;

/// <summary>Shared write-then-rename helper so a crash mid-write never leaves a
/// truncated report on disk. Used by both Benchmarks and RAG eval export,
/// which previously carried their own near-identical copies.</summary>
public static class AtomicFile
{
    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
