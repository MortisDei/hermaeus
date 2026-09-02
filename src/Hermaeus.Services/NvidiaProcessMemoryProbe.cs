using System.Runtime.InteropServices;

namespace Hermaeus.Services;

/// <summary>
/// Best-effort PID-scoped NVIDIA memory observation without a package or a
/// device-wide attribution guess. Unsupported drivers remain Unknown.
/// </summary>
internal static class NvidiaProcessMemoryProbe
{
    private const int NvmlSuccess = 0;
    private const int NvmlInsufficientSize = 7;
    private const ulong NvmlValueNotAvailable = ulong.MaxValue;

    public static bool TryGetBytes(int processId, out long bytes)
    {
        bytes = 0;
        if (!OperatingSystem.IsWindows())
            return false;

        var initialized = false;
        try
        {
            if (nvmlInit_v2() != NvmlSuccess)
                return false;
            initialized = true;

            uint deviceCount = 0;
            if (nvmlDeviceGetCount_v2(ref deviceCount) != NvmlSuccess)
                return false;

            ulong total = 0;
            var found = false;
            for (uint deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(deviceIndex, out var device) != NvmlSuccess)
                    continue;

                var count = 32u;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var processes = new NvmlProcessInfo[count];
                    var result = nvmlDeviceGetComputeRunningProcesses_v2(device, ref count, processes);
                    if (result == NvmlInsufficientSize && count > processes.Length && count <= 4096)
                        continue;
                    if (result != NvmlSuccess)
                        break;

                    foreach (var process in processes.AsSpan(0, checked((int)Math.Min(count, (uint)processes.Length))))
                    {
                        if (process.Pid != processId || process.UsedGpuMemory == NvmlValueNotAvailable)
                            continue;
                        total = checked(total + process.UsedGpuMemory);
                        found = true;
                    }
                    break;
                }
            }

            if (!found || total > long.MaxValue)
                return false;
            bytes = (long)total;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException
            or SEHException
            or OverflowException
            or ExternalException)
        {
            return false;
        }
        finally
        {
            if (initialized)
            {
                try { _ = nvmlShutdown(); }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException or ExternalException) { }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlProcessInfo
    {
        public uint Pid;
        public ulong UsedGpuMemory;
    }

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetCount_v2(ref uint deviceCount);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetComputeRunningProcesses_v2(
        IntPtr device, ref uint infoCount, [Out] NvmlProcessInfo[] infos);
}
