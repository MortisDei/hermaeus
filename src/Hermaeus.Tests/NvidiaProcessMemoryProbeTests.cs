using System.Runtime.InteropServices;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class NvidiaProcessMemoryProbeTests
{
    [Fact]
    public void Compute_v2_layout_matches_native_header_including_MIG_fields()
    {
        Assert.Equal(24, Marshal.SizeOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>());
        Assert.Equal(0, Marshal.OffsetOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>("Pid").ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>("UsedGpuMemory").ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>("GpuInstanceId").ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>("ComputeInstanceId").ToInt32());
    }

    [Fact]
    public void Full_native_v2_array_preserves_last_record_and_unknown_sentinel()
    {
        // Write independently specified native offsets into a safely sized block.
        // This detects stride regressions without corrupting the test host.
        const int count = 32;
        const int nativeStride = 24;
        var memory = Marshal.AllocHGlobal(count * nativeStride);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var record = IntPtr.Add(memory, i * nativeStride);
                Marshal.WriteInt32(record, 0, 1000 + i);
                Marshal.WriteInt64(record, 8, i == count - 1 ? -1 : 4096L * i);
                Marshal.WriteInt32(record, 16, -1);
                Marshal.WriteInt32(record, 20, -1);
            }
            for (var i = 0; i < count; i++)
            {
                var record = Marshal.PtrToStructure<NvidiaProcessMemoryProbe.NvmlProcessInfo>(
                    IntPtr.Add(memory, i * Marshal.SizeOf<NvidiaProcessMemoryProbe.NvmlProcessInfo>()));
                Assert.Equal((uint)(1000 + i), record.Pid);
                Assert.Equal(i == count - 1 ? ulong.MaxValue : (ulong)(4096L * i), record.UsedGpuMemory);
                Assert.Equal(uint.MaxValue, record.GpuInstanceId);
                Assert.Equal(uint.MaxValue, record.ComputeInstanceId);
            }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
