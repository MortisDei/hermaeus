"""Safely measure NVML v2 writes on Windows x64 without an undersized buffer.

Optional --retain-cuda-context creates one temporary context in this process.
It is released in finally. No app settings or registry keys are changed.
"""
import argparse
import ctypes as c
import json
import os
from pathlib import Path


class ProcessInfoV2(c.Structure):
    _fields_ = [("pid", c.c_uint), ("memory", c.c_ulonglong),
                ("gpu_instance", c.c_uint), ("compute_instance", c.c_uint)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--retain-cuda-context", action="store_true")
    args = parser.parse_args()
    if os.name != "nt" or c.sizeof(c.c_void_p) != 8:
        raise SystemExit("This diagnostic requires Windows and 64-bit Python.")
    system = Path(os.environ["SystemRoot"]) / "System32"
    nvml = c.CDLL(str(system / "nvml.dll"))
    nvml.nvmlDeviceGetCount_v2.argtypes = [c.POINTER(c.c_uint)]
    nvml.nvmlDeviceGetHandleByIndex_v2.argtypes = [c.c_uint, c.POINTER(c.c_void_p)]
    nvml.nvmlDeviceGetComputeRunningProcesses_v2.argtypes = [c.c_void_p, c.POINTER(c.c_uint), c.c_void_p]
    result = nvml.nvmlInit_v2()
    if result != 0:
        raise SystemExit(f"NVML initialization unavailable: result={result}")
    cuda = None
    retained = False
    try:
        if args.retain_cuda_context:
            cuda = c.WinDLL(str(system / "nvcuda.dll"))
            cuda.cuDevicePrimaryCtxRetain.argtypes = [c.POINTER(c.c_void_p), c.c_int]
            cuda.cuDevicePrimaryCtxRelease_v2.argtypes = [c.c_int]
            result = cuda.cuInit(0)
            if result != 0:
                raise RuntimeError(f"CUDA initialization unavailable: result={result}")
            context = c.c_void_p()
            result = cuda.cuDevicePrimaryCtxRetain(c.byref(context), 0)
            retained = result == 0
            if not retained:
                raise RuntimeError(f"CUDA context unavailable: result={result}")
        devices = c.c_uint()
        result = nvml.nvmlDeviceGetCount_v2(c.byref(devices))
        if result != 0:
            raise RuntimeError(f"NVML enumeration unavailable: result={result}")
        for index in range(devices.value):
            device = c.c_void_p()
            result = nvml.nvmlDeviceGetHandleByIndex_v2(index, c.byref(device))
            if result != 0:
                print(json.dumps({"device": index, "handle_result": result}))
                continue
            capacity = 32
            stride = c.sizeof(ProcessInfoV2)
            assert stride == 24
            extent = capacity * stride
            buffer = (c.c_ubyte * (extent + 64))(*([0xA5] * (extent + 64)))
            count = c.c_uint(capacity)
            result = nvml.nvmlDeviceGetComputeRunningProcesses_v2(device, c.byref(count), buffer)
            changed = [i for i, value in enumerate(buffer) if value != 0xA5]
            print(json.dumps({
                "device": index, "result": result, "count": count.value,
                "capacity": capacity, "v2_stride": stride,
                "highest_changed_offset": max(changed, default=-1),
                "changed_bytes_beyond_legacy_512": sum(i >= 512 for i in changed),
                "v2_guard_intact": all(value == 0xA5 for value in buffer[extent:]),
                "temporary_cuda_context": retained,
            }))
            if any(value != 0xA5 for value in buffer[extent:]):
                raise RuntimeError("Native write exceeded the correct v2 buffer.")
    finally:
        try:
            if retained:
                print(json.dumps({"cuda_context_release": cuda.cuDevicePrimaryCtxRelease_v2(0)}))
        finally:
            print(json.dumps({"nvml_shutdown": nvml.nvmlShutdown()}))


if __name__ == "__main__":
    main()
