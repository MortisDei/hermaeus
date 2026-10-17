using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>Explicit export of session bytes to the selected local file, with atomic replacement.</summary>
public static class MediaFileService
{
    public static async Task<string> ReadWorkflowAsync(string path, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        ValidatePath(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 1_048_576) throw new InvalidDataException("Workflow exceeds the 1 MiB import budget.");
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (output.Length + count > 1_048_576) throw new InvalidDataException("Workflow exceeds the 1 MiB import budget.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return new System.Text.UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
    }

    public static async Task SaveAsync(string path, GeneratedMedia media, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), Path.GetExtension(media.SuggestedFileName), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the generated media's file extension.", nameof(path));
        if (media.Data.Length is < 1 or > MediaGenerationService.MaxOutputBytes)
            throw new InvalidDataException("The generated file exceeds the export budget.");
        ValidatePath(path);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing export directory.");
        var temp = Path.Combine(directory, ".hermaeus-media-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            ValidatePath(temp);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(temp, options))
            {
                await output.WriteAsync(media.Data, ct);
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            ValidatePath(path);
            ValidatePath(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // Only our exact staging path is eligible for cleanup. Do not hide
            // a failed cleanup as a successful export.
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void ValidatePath(string path)
    {
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
        {
            try
            {
                if ((File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Media export refuses reparse-point paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
