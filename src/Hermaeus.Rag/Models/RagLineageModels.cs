using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hermaeus.Rag.Models;

public enum RagSourceKind
{
    LocalFile,
    WebUrl,
    Legacy
}

public enum RagSourceRevisionState
{
    Staged,
    Current,
    Superseded
}

public enum RagDatasetGenerationState
{
    Staged,
    Current,
    Superseded
}

public sealed record RagSourceDescriptor(
    string SourceId,
    string DatasetId,
    string? WatchRootId,
    string RelativeLocator,
    RagSourceKind Kind,
    string? RootIdentity = null);

public sealed record RagSourceRevision(
    string RevisionId,
    string SourceId,
    string ContentHash,
    string SourceEvidence,
    string EmbeddingIdentity,
    RagSourceRevisionState State,
    DateTime CreatedAtUtc,
    string? PreviousRevisionId = null,
    DateTime? SourceModifiedUtc = null);

public sealed record RagDatasetGeneration(
    string GenerationId,
    string DatasetId,
    string EmbeddingIdentity,
    int EmbeddingDimensions,
    RagDatasetGenerationState State,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    int ChunkCount,
    string? PreviousGenerationId = null);

/// <summary>
/// The durable identity placed on a citation. A path is useful display data,
/// but it cannot prove which indexed bytes supplied an answer.
/// </summary>
public static class RagCitationIdentity
{
    public static string BuildLocator(RagChunk chunk)
    {
        var dataset = string.IsNullOrWhiteSpace(chunk.DatasetId) ? "unknown" : chunk.DatasetId;
        var generation = string.IsNullOrWhiteSpace(chunk.GenerationId) ? "legacy" : chunk.GenerationId;
        var source = string.IsNullOrWhiteSpace(chunk.SourceId) ? chunk.SourcePath : chunk.SourceId;
        var revision = string.IsNullOrWhiteSpace(chunk.SourceRevisionId) ? chunk.SourceHash : chunk.SourceRevisionId;
        return $"rag:{dataset}/generation:{generation}/source:{source}/revision:{revision}/content:{chunk.SourceHash}";
    }
}

public static class RagSourceIdentity
{
    public static string ForWatchedRoot(string datasetId, string root)
    {
        var normalized = NormalizePath(root);
        return $"watch:{Hash($"{datasetId}\0{normalized}")}";
    }

    public static string ForSource(string datasetId, RagWatchedSource? watched, string sourcePath)
    {
        var normalized = NormalizePath(sourcePath);
        if (watched is null)
            return $"file:{Hash($"{datasetId}\0manual\0{normalized}")}";

        var rootId = string.IsNullOrWhiteSpace(watched.WatchRootId)
            ? ForWatchedRoot(datasetId, watched.Root)
            : watched.WatchRootId;
        var relative = Path.GetRelativePath(watched.Root, sourcePath).Replace('\\', '/');
        return $"file:{Hash($"{datasetId}\0{rootId}\0{NormalizeRelative(relative)}")}";
    }

    public static string RelativeLocator(RagWatchedSource? watched, string sourcePath) => watched is null
        ? Path.GetFileName(sourcePath)
        : Path.GetRelativePath(watched.Root, sourcePath).Replace('\\', '/');

    public static string? TryGetRootIdentity(string root)
    {
        try
        {
            var info = new DirectoryInfo(root);
            if (!info.Exists)
                return null;

            if (OperatingSystem.IsLinux() && TryGetLinuxIdentity(info.FullName, out var unix))
                return $"directory:unix:{unix}";

            if (OperatingSystem.IsWindows() && TryGetWindowsIdentity(info.FullName, out var windows))
                return $"directory:windows:{windows.VolumeSerialNumber:X}:{windows.FileIndex:X}";

            return info.CreationTimeUtc == DateTime.MinValue
                ? null
                : $"directory:fallback:{Hash($"{NormalizePath(info.FullName)}\0{info.CreationTimeUtc.Ticks}")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static bool TryGetWindowsIdentity(string path, out WindowsIdentity identity)
    {
        identity = default;
        using var handle = CreateFile(
            path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            return false;

        identity = new WindowsIdentity(
            info.VolumeSerialNumber,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        return true;
    }

    private static bool TryGetLinuxIdentity(string path, out string identity)
    {
        identity = string.Empty;
        const int atFdcwd = -100;
        const uint statxAll = 0x0fff;
        const uint statxInode = 0x0100;
        const uint statxBirthTime = 0x0800;
        if (statx(atFdcwd, path, 0, statxAll, out var extended) == 0
            && (extended.Mask & statxInode) != 0)
        {
            var birth = (extended.Mask & statxBirthTime) != 0
                ? $":{extended.BirthTime.Seconds}:{extended.BirthTime.Nanoseconds}"
                : string.Empty;
            identity = $"{extended.DeviceMajor:X}:{extended.DeviceMinor:X}:{extended.Inode:X}{birth}";
            return true;
        }

        if (stat(path, out var basic) == 0)
        {
            identity = $"{basic.Device:X}:{basic.Inode:X}";
            return true;
        }

        return false;
    }

    private readonly record struct WindowsIdentity(uint VolumeSerialNumber, ulong FileIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnixStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong LinkCount;
        public uint Mode;
        public uint UserId;
        public uint GroupId;
        public uint Padding;
        public ulong SpecialDevice;
        public long Size;
        public long BlockSize;
        public long Blocks;
        public long AccessSeconds;
        public ulong AccessNanoseconds;
        public long ModifySeconds;
        public ulong ModifyNanoseconds;
        public long ChangeSeconds;
        public ulong ChangeNanoseconds;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public LinuxStatxTimestamp AccessTime;
        public LinuxStatxTimestamp BirthTime;
        public LinuxStatxTimestamp ChangeTime;
        public LinuxStatxTimestamp ModifyTime;
        public uint DeviceMajor;
        public uint DeviceMinor;
        public uint SpecialDeviceMajor;
        public uint SpecialDeviceMinor;
        public ulong MountId;
        public uint DirectIoMemoryAlignment;
        public uint DirectIoOffsetAlignment;
        public ulong SpareU0;
        public ulong SpareU1;
        public ulong SpareU2;
        public ulong SpareU3;
        public ulong SpareU4;
        public ulong SpareU5;
        public ulong SpareU6;
        public ulong SpareU7;
        public ulong SpareU8;
        public ulong SpareU9;
        public ulong SpareU10;
        public ulong SpareU11;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("libc", EntryPoint = "stat", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int stat(string path, out UnixStat buffer);

    [DllImport("libc", EntryPoint = "statx", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int statx(
        int directoryFileDescriptor, string path, int flags, uint mask, out LinuxStatx buffer);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file, out WindowsFileInformation information);

    private static string NormalizePath(string path)
    {
        var normalized = Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..32];
}
