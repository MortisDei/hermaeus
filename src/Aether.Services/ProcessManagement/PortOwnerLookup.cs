using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Aether.Services.ProcessManagement;

/// <summary>Best-effort identity of the process listening on a port (r9 02-server-lifecycle.md 2.2/2.3).</summary>
public sealed record PortOwnerInfo(int Pid, string ProcessName, string? ExecutablePath);

/// <summary>
/// Named-owner port diagnostics: whether a port is already bound (cross
/// platform, via <see cref="IPGlobalProperties"/>) and, best-effort on
/// Windows, which process bound it. Kept behind an interface so callers
/// (port preflight, orphan detection) are unit-testable with a fake instead
/// of needing a real second process bound to a real port.
/// </summary>
public interface IPortOwnerLookup
{
    /// <summary>True if anything is listening on the given loopback TCP port.</summary>
    bool IsPortListening(int port);

    /// <summary>PID/name/executable path of the process listening on the given port, or null if not listening or the lookup failed.</summary>
    PortOwnerInfo? FindOwner(int port);
}

public sealed class SystemPortOwnerLookup : IPortOwnerLookup
{
    public bool IsPortListening(int port) =>
        IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(ep => ep.Port == port);

    /// <summary>
    /// Windows-only: maps the port to an owning PID via GetExtendedTcpTable,
    /// then resolves the process name and executable path. Any failure
    /// (permission, process exited mid-lookup, non-Windows) yields null; the
    /// caller falls back to naming the port alone.
    /// </summary>
    public PortOwnerInfo? FindOwner(int port)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var pid = FindOwningPid(port);
            if (pid is null) return null;

            using var process = Process.GetProcessById(pid.Value);
            string? path = null;
            try { path = process.MainModule?.FileName; }
            catch { /* access denied or exited; port + PID + name still get reported */ }

            return new PortOwnerInfo(pid.Value, process.ProcessName, path);
        }
        catch
        {
            return null;
        }
    }

    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;

    private static int? FindOwningPid(int port)
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);
        if (bufferSize <= 0) return null;

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0) != 0)
                return null;

            var numEntries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                var localPort = ((row.LocalPort & 0x0000FF00) >> 8) | ((row.LocalPort & 0x000000FF) << 8);
                if (localPort == port)
                    return (int)row.OwningPid;

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);
}

public static class PortOwnerLookup
{
    public static readonly IPortOwnerLookup Default = new SystemPortOwnerLookup();
}
