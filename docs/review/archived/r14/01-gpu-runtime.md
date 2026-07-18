# 01 - GPU runtime (build variant and offload)

The owner's 2026-07-18 field log shows CPU-class inference on every
send: prompt processing at 51-65 tokens/sec and generation at 8.6-13
tokens/sec for a 4B-class model. Two code facts guarantee this outcome
for every Windows user:

- `LlamaServerSetupService` only ever downloads the CPU build. The
  pinned suffix for WinX64 is `-bin-win-cpu-x64.zip`
  (LlamaServerSetupService.cs:342) and `SelectDownloadAsset`
  (LlamaServerSetupService.cs:326-336) matches the same suffix for the
  latest-release path, so both install and update land a binary with
  no GPU backend at all.
- Even on a GPU-capable binary, offload defaults to nothing:
  `GpuLayers = 0` in the default managed-server config
  (SettingsService.cs:328) and `BuildLaunchArguments` omits
  `--n-gpu-layers` entirely when it is 0
  (ServerProcessManager.cs:380-384).

r13 gave us the hardware truth to fix this properly:
`SystemInfoService` now reports real GPUs with VRAM on Windows via the
registry probe, and `GetHardwareProfileAsync` returns
`(TotalRam, MaxVram, GpuName)`. r14 makes the runtime consume it.

## 1.1 Variant-aware llama.cpp asset selection

Add a runtime variant concept to `LlamaServerSetupService`:

- `LlamaRuntimeVariant { Auto, Cpu, Cuda, Vulkan }`, persisted in
  settings (DataManagement or a new LlamaRuntime block), default Auto.
- Auto resolves against the hardware snapshot: NVIDIA GPU name ->
  Cuda; any other real GPU (AMD, Intel) -> Vulkan; no GPU or probe
  unavailable -> Cpu. Vendor classification is a pure static function
  over the GPU name string with tests (NVIDIA/GeForce/RTX/Quadro ->
  nvidia; Radeon/AMD -> amd; Arc/Iris/UHD/Intel -> intel).
- `SelectDownloadAsset` gains the variant parameter: match the release
  asset by os/arch suffix plus variant token ("-cuda-", "-vulkan-",
  "-cpu-"). Exact asset names must be re-verified against the live
  GitHub API at implementation time, same discipline as r11 1.2; do
  not trust memory of the naming scheme.
- Non-Windows platforms keep today's selection untouched this round.

Acceptance criteria:
- Pure tests: asset lists modeled on a real release resolve to the
  expected asset for every (platform, variant) pair, and Auto picks
  the right variant for representative GPU names.
- The Services/Doctor install UI names the variant it is about to
  download ("llama-server b10066, Windows x64 Vulkan").
- Explicit setting always wins over Auto.

## 1.2 CUDA companion runtime and launch verification

CUDA builds require the separate `cudart-...` companion archive that
llama.cpp publishes alongside the main zip. When variant Cuda is
selected, download and extract both into the same versioned directory.

After any install or update, verify the binary actually starts:
run `llama-server --version` (the machinery exists,
DoctorService.Runtime.cs `ReadLlamaServerVersionAsync`). If it fails
to execute (missing driver, missing DLL), report the failure and fall
back to the Cpu variant rather than leaving a broken configured path.

Acceptance criteria:
- Cuda install produces a directory where llama-server.exe starts.
- A simulated launch failure (test seam around the version probe)
  falls back to Cpu and logs one clear runtime-log line.
- No fallback loop: Cpu is terminal.

## 1.3 Offload defaults that use the GPU

With a GPU build installed, `GpuLayers = 0` still means CPU inference.
Change the semantics: `GpuLayers = -1` means "all layers", rendered as
`--n-gpu-layers 999`, and becomes the default for new managed servers
when a real GPU was detected. 0 remains "explicitly CPU" and existing
saved configs are not rewritten (r12 settings-lifecycle rules); the
auto-tune path (`ServerProcessManager.AutoTuneAsync`) already probes
downward from a requested layer count and its persisted
`LlamaTuneProfile` continues to win when present.

Acceptance criteria:
- `BuildLaunchArguments` test matrix: -1 -> `--n-gpu-layers 999`,
  0 -> flag omitted, N>0 -> N (existing behavior).
- Setup wizard / starter flow creates the chat server with -1 when the
  hardware profile has VRAM, 0 otherwise.
- Services page shows the effective offload ("all layers", "0 (CPU)",
  or the tuned N) instead of a bare spinner value.

## 1.4 Doctor advisory: GPU present, CPU inference configured

Add a Doctor advisory that fires when the hardware profile contains a
real GPU but either (a) the installed llama-server build is a CPU
variant, or (b) the chat server's effective gpu-layers is 0. Wording
should state the measured consequence, e.g. "your prompts are read at
CPU speed", and deep-link the fix (install variant / edit server).

Acceptance criteria:
- Advisory appears exactly under those conditions, with tests over the
  pure decision function.
- Resolving either condition clears it on the next Doctor run.
