# R33 Windows native heap-corruption investigation

Date: 2026-09-22. Baseline: clean `r33/round`, `4f7ad9b5f71ca5896d86b77ac0094e7ab58af133`.
No inherited modifications. The installed package was inspected, not replaced.

## Conclusion and evidence limits

A confirmed NVML ABI mismatch permits native writes past a managed array in
Hermaeus.Desktop. This is the leading explanation for the recurring failures.
The installed NVIDIA driver reproduced the write extent safely using a correctly
sized buffer. Historical attribution remains unproven without a crash dump or
native-call trace: the events do not record the writing instruction or NVML
process count. `ntdll.dll` is the detection location, not evidence that Windows
originated the corruption. Identical offsets are not unique root causes.

The shipped `dist/hermaeus-0.41.0-beta-win-x64/app/Hermaeus.Services.dll` was
inspected through reflection and contains only `Pid` and `UsedGpuMemory` in
`NvidiaProcessMemoryProbe.NvmlProcessInfo`. Reflection confirmed a 16-byte
record and the `nvmlDeviceGetComputeRunningProcesses_v2` entry point in
`nvml.dll` with Cdecl calling convention. Its SHA256 at investigation time was
`3F8F5CC830DF8E9AD85E9F1520D2B944E345CB56753F4C5662C0459D304DB9A4`.
Version 0.41.0.0 alone is not a source-commit identity.

## Windows and runtime correlation

Event 1000 was read directly from the local Application log. All three report
Hermaeus.Desktop.exe 0.41.0.0, ntdll.dll 10.0.26100.9444, exception `0xc0000374`,
offset `0x117eb5`, and the same packaged executable path. Times below are AEST
(UTC+10); application log timestamps use UTC.

| Crash (AEST) | Desktop PID | Last preceding runtime entry (UTC) | Correlation |
| --- | --- | --- | --- |
| 20 Sep 20:08:51 | 8896 / 0x22C0 | 10:08:28.2225394 | Chat completed at 10:08:25; recall indexing deferred because traces.db-journal disappeared; about 23 seconds before detection. |
| 20 Sep 20:21:25 | 3080 / 0xC08 | 10:21:01.6735401 | Chat completed at 10:20:59; recall backfill successfully embedded two items; about 23 seconds before detection. |
| 21 Sep 20:32:10 | 17648 / 0x44F0 | 10:32:09.5764534 | Runtime task 4236 completed a 259-token generation and reported graphs reused=4432; under one second before detection. |

Source: `C:\Hermaeus\logs\runtime.log`, which retained all three intervals.
The log is aggregate, not fully desktop-PID stamped; these are temporal
correlations, not complete per-process traces. No telemetry open/close or NVML
entry markers existed. The last interval contains repeated runtime work while
the owner was away, but does not identify which caller owned every request.
Do not relabel it as an observed benchmark merely because the timings resemble one.

Event 1001 adds `PCH_C8_FROM_ntdll+0x161914`. The first and third use
`StackHash_5520` / bucket 2099417887907602275; the second uses `StackHash_5482` /
bucket 2117808972180010312. This supports recurring detection, not a shared
writing stack. Report IDs respectively:

- `a4cde06c-8fa8-492f-80a7-0c78c41d1c36`
- `cf8b672a-282f-424e-9342-be38ed3c320b`
- `3bee66db-8012-41eb-8ad2-d7fdfb207fb6`

`C:\Hermaeus\lifecycle.json` reports startup 21 Sep 10:08:58.427919Z,
`CleanExit=false`, last operation `running` at 10:09:05.4297457Z. No shutdown or
managed fatal record closes the latest runtime log. WER archive directories
exist, but their report contents remained ACL-denied even in the normal host
runner. No Hermaeus dump was found in the user's default CrashDumps directory.
Loaded-module lists, exception stacks, and damaged allocation provenance were
therefore unavailable. No permissions or WER settings were changed.

## Proven native corruption mechanism

`NvidiaProcessMemoryProbe` imports `nvmlDeviceGetComputeRunningProcesses_v2`
but supplied the legacy two-field structure. NVIDIA's
[v2 structure documentation](https://docs.nvidia.com/deploy/archive/R510/nvml-api/structnvmlProcessInfo__v2__t.html)
includes two additional 32-bit MIG identifiers, even on non-MIG hardware.
On Windows x64 the offsets are 0, 8, 16, 20 and the stride is 24 bytes.
The old managed stride was 16 bytes.

The initial array advertised 32 records while reserving only 512 bytes.
22 native records require 528 bytes; 32 require 768. Even smaller results can
be read at the wrong stride, producing incorrect PID/memory interpretations.
WDDM unavailable-memory sentinels do not prevent the record writes. A catch
around P/Invoke cannot repair an already-overwritten allocation.

Safe local driver experiment, separate 64-bit Python 3.14.0 process; installed
System32 nvml.dll file/product version 8.17.16.1074 (current experiment version,
not an inferred historical loaded-module version):

1. A correctly sized 768-byte buffer plus 64 guard bytes returned 21 records,
   success, highest changed offset 503; guard intact.
2. One temporary CUDA primary context in the probe process increased the result
   to 22 records. The driver wrote through offset 527, changing 16 bytes beyond
   the old allocation boundary. The correct v2 guard stayed intact.
3. Context release and NVML shutdown both returned success. The saved script
   reproduced the same 22-record/offset-527 result after the session resumed.

No undersized buffer was passed to the driver, no application process was
crashed, and no runtime was stopped. This proves the overwrite mechanism on
this host, not the historical process counts. Run
`python scripts/probe-nvml-layout.py`; explicitly add `--retain-cuda-context`
to create/release one temporary context. Counts vary, and fewer than 22 records
do not disprove the ABI defect. The script needs Windows x64 Python and the
installed NVIDIA DLLs, but no Python packages.

## Telemetry lifetime and sibling call paths

- Chat's flyout started the singleton sampler but never called `CloseAsync`
  when dismissed. Polling could continue every second after the UI disappeared,
  including while unattended. Closing is a lead, not proof of the writing site.
- The first capture was outside the task joined by Stop. An old capture could
  finish after close/restart, append to a replacement series or throw its
  identity guard, and queue UI updates after close. The VM lacked a generation
  check at dispatcher execution time. These are managed lifetime defects;
  they do not independently explain raw heap writes.
- Initial and periodic captures could overlap. The async method's synchronous
  NVML prefix could run on the UI thread under the cache lock. Cancelling a
  caller cancelled its wait for the shared probe, not NVML itself.
- BenchmarkService.SampleBenchmarkAuthorityAsync and LabRecipeService's runtime
  observation path use the same source without opening the flyout. They make
  NVML relevant to unattended work as well as UI interaction.
- SystemInfoService uses OS counters and external GPU utilities, not another
  in-process NVML binding. Native init/shutdown calls remain balanced; no
  evidence establishes a driver reference-count race.

Corrections: exact v2 struct, joined first/periodic captures, serialized session
transitions, generation guards, flyout-close/detach/context-change teardown,
cancellation during identity lookup, and worker-thread GPU probes. Existing
in-flight cached probes are reused rather than duplicated after two seconds.
Native calls are not claimed cancellable: a shared probe already executing may
finish after its subscriber closes; no new polling session is left behind.

## Other native components and independent bugs

| Component | Exposure and evidence |
| --- | --- |
| Avalonia/Skia/HarfBuzz and Windows rendering/input | In-process desktop dependencies; popup close can allocate/free native resources and expose earlier damage. No dump ties rendering to the original write. |
| NVIDIA NVML | Direct in-process P/Invoke; proven ABI overwrite, strongest lead. NVIDIA/CUDA code inside llama-server is in a different process and cannot ordinarily overwrite the desktop heap. |
| SQLite | In-process native databases used during ordinary recall/storage work. No demonstrated SQLite memory fault. |
| ONNX Runtime, native Kokoro and RAG reranker | In-process inference. Latest log shows Kokoro preview completed at 10:24:05, about eight minutes before the last crash. No concurrent disposal was logged. Separate session lifetime defects were repaired below. |
| WinMM playback/capture | Latest runtime-ready cue completed at 10:27:11. Capture uses native headers/pinned buffers, but no evidence places active microphone capture at the crashes. Device teardown remains owner-live, not a claimed cause. |
| Hotkey callback, job objects, TCP ownership, memory/credential/file APIs | Other direct interop; callback is rooted, job/TCP buffers have explicit size/free ownership. No analogous v2 record mismatch found in this bounded audit. This is not a general native-safety certification. |

Independent corrections requested by the owner:

- Database-size enumeration could stat a SQLite journal after SQLite deleted
  it. This fits the first crash's managed exception, but does not explain native
  corruption. Missing individual files are tolerated; unrelated access failures
  are not masked. The sampled Process is now disposed.
- Kokoro inference bypassed its load/install gate, and disposal bypassed that
  gate. The reranker likewise released its gate after loading, before scoring,
  and disposed without acquiring it. Session load, inference, replacement, and
  async disposal now share each model's existing gate. Disposed models cannot
  be resurrected. Provider async disposal preserves DI shutdown behavior.
  No admission, model-hash, Agent, or filesystem boundary was relaxed.

## Coverage and verification limits

Existing telemetry tests primarily exercised fake sources, steady-state
start/stop, process identity, and Unknown semantics. The real-process smoke test
could call NVML but never checked ABI layout or controlled record count. It
could pass with few GPU processes while leaving the overwrite latent. Managed
suite success is not native heap-integrity evidence.

Added regressions cover v2 offsets/stride, multi-record decoding and WDDM
sentinels, stopping a cancellation-resistant initial capture, overlapping
restart, close during capture, queued dispatcher publication, terminal disposal,
SQLite journal disappearance, and both ONNX disposal gates. ONNX tests validate
the ownership protocol without loading a live model; they are not a hardware
inference stress pass. AXAML event wiring compiles, but actual flyout interaction
and long unattended dogfood remain owner-live gates.

- Debug and Release solution builds before the test-fixture-only correction:
  zero warnings/errors in each.
- Final focused regression set: 30 passed, zero failed/skipped.
- Final coverage script: 2,822 passed, zero failed/skipped (6m03s);
  54,125/81,716 lines = 66.24%, above the enforced 60% floor.
  The collector wrote its temporary report outside the checkout; the script
  removed it afterward.
- Pre-fixture Debug full suite: 2,822 passed, zero failed/skipped (3m46s).
- Pre-fixture Release full suite: 2,822 passed, zero failed/skipped (3m42s).
- Coverage exposed test fixtures that named plain text `llama-server.exe` and then
  invoked the real `--help` probe. Windows showed a modal loader error and hung
  the runner. The probe tests now use a runnable host stub; the affected 32
  tests pass with coverage. Both coverage scripts now reject nonzero test exits,
  missing reports, and measured line coverage below the floor.
- Pre-fixture TRX files: `focused-final.trx`, `full-debug-final.trx`, and
  `full-release-final.trx` under `%TEMP%\hermaeus-crash-investigation-tests`.
- Safe native script: 22 records, highest changed offset 527, 16 changed bytes
  beyond the legacy boundary, intact v2 guard; context release/shutdown both 0.
- `git diff --check` passed. No generated test output entered the checkout.
  No packaging, release, or Windows registry change was performed.
- Native GUI/long unattended testing and historical dump attribution remain
  explicitly unverified.

## Next occurrence: collect the writing context

The patched source accepts `HERMAEUS_NATIVE_PROBE_TRACE=1` at process startup.
This enables persisted capture series and process-start identity, correlated
native probe entry/exit, desktop/runtime PID, NVML
capacity/stride/returned-count/result, and shutdown breadcrumbs through the
existing redacted, bounded RuntimeLogService. Preserve runtime.log and
lifecycle.json immediately after failure, before another startup replaces the
journal. No prompts, tokens, or other processes' identities are added by this
trace. A missing exit marker narrows location but is not an allocation stack.

In a new PowerShell session, after closing any existing desktop instance, run
the patched source build (the unchanged dist package lacks these diagnostics):

```powershell
$env:HERMAEUS_NATIVE_PROBE_TRACE = '1'
dotnet run --project src/Hermaeus.Desktop -c Release --no-build
```

This uses the owner's normal settings/data root. Close the app cleanly after
reproduction; the environment switch is confined to that shell and its children.

For full dumps, use an elevated PowerShell terminal. First inspect and export
any existing per-image configuration. Set only this executable's LocalDumps
values. These are instructions, not changes performed by this pass:

```powershell
$dumpKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Hermaeus.Desktop.exe'
Get-ItemProperty -LiteralPath $dumpKey -ErrorAction SilentlyContinue
New-Item -Path $dumpKey -Force | Out-Null
New-ItemProperty -LiteralPath $dumpKey -Name DumpType -PropertyType DWord -Value 2 -Force | Out-Null
New-ItemProperty -LiteralPath $dumpKey -Name DumpCount -PropertyType DWord -Value 3 -Force | Out-Null
```

Without an existing DumpFolder override, WER uses `%LOCALAPPDATA%\CrashDumps`.
Restore previous per-image values afterward. Full dumps can contain
conversations and secrets; retain them locally. See
[Microsoft LocalDumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps).

Preserve exact executable/Services DLL/native DLL versions, symbols and SHA256
hashes alongside the dump. In WinDbg start with `!analyze -v`, `.ecxr`, `kv`,
`~* kb`, and `lm`; examine the damaged allocation and allocation/free stacks
when available. Heap failure-handling frames remain detection evidence.

For bounded reproduction with Windows Debugging Tools/Application Verifier:

```text
gflags /p /enable Hermaeus.Desktop.exe /full
# Start a NEW process under WinDbg; preserve the first verifier break.
gflags /p /disable Hermaeus.Desktop.exe
```

[Full page heap](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/example-12---using-page-heap-verification-to-find-a-bug)
can catch native heap misuse closer to the write and has substantial overhead.
It does not guarantee trapping an overrun inside a pinned CLR heap segment.
For NVML, break on `nvmlDeviceGetComputeRunningProcesses_v2`, record supplied
capacity and returned count, and compare actual array extent with the 24-byte
ABI. Keep a dump on the first verifier/access-violation break as well as the
terminal heap-corruption event.

Exercise open/close, rapid close during initial sampling, navigation, runtime
restart, benchmark/Lab completion, and unattended operation on the patched build.
Do not disable heap checks, catch-and-continue corrupted-state failures,
substitute device totals for process VRAM, or weaken process identity.
The old installed package remains unfixed until rebuilt/replaced by the owner.
