using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aether.Services.ProcessManagement;

/// <summary>
/// Assigns a child process to a Windows job object with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, so the OS kills it when the app
/// process dies, however it dies (r9 02-server-lifecycle.md 2.1). Without
/// this, a crashed app leaves orphaned llama-server/voice-engine children
/// holding ports and GPU memory that block the next launch.
/// </summary>
public interface IProcessJobObject
{
    /// <summary>Assigns <paramref name="process"/> to the shared job object. Returns false on any failure; never throws.</summary>
    bool TryAssign(Process process);
}

/// <summary>Non-Windows / test default: no-op, matching the app's Windows-first posture (no Linux job-object equivalent).</summary>
public sealed class NullProcessJobObject : IProcessJobObject
{
    public bool TryAssign(Process process) => false;
}

/// <summary>
/// Real Windows implementation. One job handle is created lazily on first use
/// and lives for the process lifetime; every managed server, auto-tune
/// probe, and voice-engine child gets assigned to it. Only the intent behind
/// the P/Invoke calls is meant to be exercised in tests; the calls
/// themselves are exempt from coverage per r9 02-server-lifecycle.md 2.1.
/// </summary>
public sealed class Win32ProcessJobObject : IProcessJobObject
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly object _gate = new();
    private IntPtr _jobHandle;
    private bool _initFailed;

    public bool TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if (!EnsureJobHandle()) return false;
            return AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    private bool EnsureJobHandle()
    {
        if (_jobHandle != IntPtr.Zero) return true;
        if (_initFailed) return false;

        lock (_gate)
        {
            if (_jobHandle != IntPtr.Zero) return true;
            if (_initFailed) return false;

            var handle = CreateJobObjectW(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                _initFailed = true;
                return false;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ptr, (uint)length))
                {
                    _initFailed = true;
                    CloseHandle(handle);
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            _jobHandle = handle;
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

/// <summary>
/// Process-wide shared instance: one job object for the app's lifetime,
/// reused by every managed server, auto-tune probe, and voice-engine
/// process, whichever manager launches them.
/// </summary>
public static class ProcessJobObject
{
    public static readonly IProcessJobObject Default =
        OperatingSystem.IsWindows() ? new Win32ProcessJobObject() : new NullProcessJobObject();
}
