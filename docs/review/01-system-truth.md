# 01 - System truth (Windows RAM, OS name, CPU name, GPU/VRAM)

Owner screenshot (System Overview, 0.17.0-alpha, Windows 11):
"Microsoft Windows 10.0.26200 (X64)", CPU "X64 - 16 threads", RAM
"unavailable available / 15.71 GB total", and no GPU section content.
All four are real code gaps in
`src/Aether.Services/SystemInfoService.cs`, not display bugs.

These fixes are not cosmetic: doc 02's fits-on-your-hardware check and
the existing `StarterModelCatalog.Recommend`
(src/Aether.Services/StarterModelCatalog.cs:57) both consume
`SystemSnapshot`, and today on a Windows AMD/Intel-GPU machine (or any
machine without `nvidia-smi` on PATH) `Recommend` always answers
"smallest tier" because `Gpus` only ever contains the "GPU probe
unavailable" placeholder.

## 1.1 Available and total RAM on Windows

`GetAvailableMemoryBytes` (SystemInfoService.cs:168-174) returns 0
unless Linux; `GetTotalMemoryBytes` (SystemInfoService.cs:148-166)
falls back to `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`, which
works but is the GC's view, not the machine's.

Fix: on Windows, P/Invoke `GlobalMemoryStatusEx` (kernel32,
`MEMORYSTATUSEX` with `ullTotalPhys`/`ullAvailPhys`). Use it for both
total and available. Keep the Linux `/proc/meminfo` path and the GC
fallback for any platform where the call fails. Put the P/Invoke in a
small `[SupportedOSPlatform("windows")]`-guarded private static class
inside SystemInfoService (repo precedent: `ProcessJobObject` in
ProcessManagement does its own P/Invoke locally).

Acceptance criteria:
- On Windows, the RAM metric shows a real nonzero available value:
  "X GB available / Y GB total".
- Non-Windows behavior unchanged.
- Unit test: the formatter path already exists; add a test that a
  snapshot with nonzero `AvailableMemoryBytes` renders both values
  (SystemOverviewViewModel.FormatBytes handles 0 as "unavailable",
  keep that).

## 1.2 Honest OS name

`OSDescription = RuntimeInformation.OSDescription`
(SystemInfoService.cs:32) reports the kernel string; Windows 11 is
build >= 22000 but still calls itself 10.0.x. The registry
`ProductName` also still says "Windows 10" on 11, so do NOT use it.

Fix: pure static formatter, e.g. `OsNameFormatter.Format(string
osDescription, Version version)` in Aether.Services:
- Windows and `version.Build >= 22000` -> "Windows 11 (build NNNNN)".
- Windows below 22000 -> "Windows 10 (build NNNNN)".
- Anything else -> the incoming description unchanged.
Optionally append the registry `DisplayVersion` value (e.g. "24H2",
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`) when readable;
failure to read is silent.

Acceptance criteria:
- System Overview on the owner's machine says "Windows 11 (build
  26200)" (plus edition/DisplayVersion if implemented), architecture
  suffix preserved.
- Pure tests for the mapper: build 26200 -> Windows 11; build 19045 ->
  Windows 10; a Linux description passes through untouched.

## 1.3 Real CPU name on Windows

`GetCpuNameAsync` (SystemInfoService.cs:135-146) reads
`/proc/cpuinfo` on Linux and otherwise returns
`RuntimeInformation.ProcessArchitecture` ("X64").

Fix: on Windows read
`HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` value
`ProcessorNameString` (string, present on every supported Windows).
Trim it. Keep the architecture fallback when the read fails.

Acceptance criteria:
- CPU metric shows the marketing name (e.g. "AMD Ryzen 7 ...") with
  the existing " - N threads" suffix.
- Registry access wrapped in try/catch; no new warnings; Linux path
  untouched.

## 1.4 GPU name and VRAM on Windows without nvidia-smi

`GetGpusAsync` (SystemInfoService.cs:73-83) tries `nvidia-smi`, then
Linux DRM, else returns nothing and the caller inserts a "GPU probe
unavailable" placeholder.

Fix: add a Windows registry fallback that enumerates the display
adapter class key
`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}`
subkeys `0000`, `0001`, ...:
- Name from `DriverDesc` (string).
- Total VRAM from `HardwareInformation.qwMemorySize` (QWORD; fall back
  to the legacy DWORD `HardwareInformation.MemorySize` when absent).
- Skip entries with no `DriverDesc` or zero memory (software
  adapters); dedupe identical DriverDesc entries keeping the largest
  memory value.
- Provider "registry", Status "OK", `MemoryUsedBytes` null (the view
  model already renders used/total only when both are present; extend
  `GpuInfoViewModel.Memory`
  (src/Aether.ViewModels/SystemOverviewViewModel.cs:111-113) to show
  "X GB total" when only the total is known).

Order of probes on Windows: nvidia-smi first (it has used-VRAM), and
when it yields nothing, the registry fallback. Verify the exact value
names on the owner's machine before finalizing (the implementer has
shell access; `Get-ItemProperty` the class key).

Also verify `SystemOverviewView.axaml` actually renders the `Gpus`
collection prominently; the owner's screenshot shows no GPU block at
all. If it is missing or buried, give it a tile alongside CPU/RAM.

Acceptance criteria:
- On the owner's machine, System Overview names the real GPU with a
  real total VRAM figure without nvidia-smi being required.
- `StarterModelCatalog.Recommend` now sees nonzero VRAM on such
  machines (no change to its logic, just to its input); existing
  Recommend tests unchanged.
- Pure test for the dedupe/skip logic via an injectable seam (e.g. the
  parser accepts a list of (name, bytes) tuples read from the
  registry; the registry read itself stays untestable-thin).

## 1.5 Snapshot reuse for the fits-check

Doc 02 item 2.5 needs total VRAM and total RAM cheaply and repeatedly
(once per Models refresh, once per HF browse). `CaptureAsync` spawns
processes and walks databases; do not call it per model row.

Fix: add `ISystemInfoService.GetHardwareProfileAsync(CancellationToken)`
returning a small cached record `HardwareProfile(long TotalRamBytes,
long MaxGpuVramBytes, string? GpuName)`; cache for the process
lifetime (hardware does not hot-change), first call does the real
probes, thread-safe via `Lazy<Task<...>>` or a `SemaphoreSlim`.

Acceptance criteria:
- Second call does no process spawn / registry walk (probe counter or
  fake seam in tests).
- `SystemOverviewViewModel.RefreshAsync` keeps using full
  `CaptureAsync` (live values wanted there).
