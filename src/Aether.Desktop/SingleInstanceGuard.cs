namespace Aether.Desktop;

/// <summary>
/// Prevents two Aether processes for the same OS user account from running
/// concurrently. Nothing coordinates writes across processes to the shared
/// SQLite data root (conversations, memory, traces, agent state), so a
/// second instance is a correctness hazard, not just a UI nuisance.
/// </summary>
internal static class SingleInstanceGuard
{
    private static FileStream? _lockStream;

    /// <summary>
    /// Default lock file location: fixed per OS user account regardless of
    /// where <c>Data.DataRootDirectory</c> has been configured to point, so
    /// the guard works even before settings are loaded.
    /// </summary>
    internal static string DefaultLockFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "aether.lock");

    /// <summary>
    /// Attempts to become the sole holder of the given lock file for the
    /// lifetime of this process. Returns false if another live process
    /// already holds it. The OS releases the underlying file handle on
    /// process exit, crash, or kill, so there is never a stale lock left
    /// behind to clean up.
    /// </summary>
    internal static bool TryAcquire(string? lockFilePath = null)
    {
        lockFilePath ??= DefaultLockFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        try
        {
            _lockStream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static void Release()
    {
        _lockStream?.Dispose();
        _lockStream = null;
    }
}
