# R34 first-party image inference: owner decision

**Decision: DEFERRED BY OWNER, 2026-10-17.**

This supersedes the earlier implementation/native-evaluation approval. No
first-party worker or native inference integration was added to Hermaeus.
The comparison and architecture below are historical research, not approved
implementation work or a claim that this runtime exists in the product.

The owner deferred this work indefinitely because existing optional Media
providers already deliver the core capability; native inference brings substantial
implementation and maintenance obligations; an SD1.5 baseline does not fulfil the
original Qwen-Image-2.1-Turbo motivation; the 6 GB/8 GB GPUs impose significant
practical constraints; and the complexity conflicts with R34's Harden, Simplify,
Accelerate objectives. Reconsider only if real usage demonstrates a compelling
requirement that existing providers cannot satisfy.

Retain ComfyUI and OpenAI-compatible integrations as optional backends. Do not
implement the proposed worker, introduce a native dependency/configuration/resource
authority, or perform further engine/model/environment/GPU dependency downloads
or installation for this work. Resume the existing R34 security, correctness,
lifecycle, simplification and measured-performance roadmap with all gates intact.

Before the deferral arrived, the authorised development download completed:
the engine and its pinned submodules were cloned outside the checkout, and the
4,265,146,304-byte SD1.5 checkpoint matched the SHA256 below. A development Vulkan
library build began using existing tools; it was stopped on deferral, and its
identified build-tool process tree was verified exited. No image was generated,
no inference library was integrated or installed, and no model was redistributed.
The source/checkpoint/build outputs were temporary development artefacts, separate
from repository changes, and were removed on deferral while retaining logs.
Build logs include upstream CMake/MSBuild warnings;
they are not a completed or zero-warning native build receipt.
Reviewed 2026-10-17 against `r34/round`, including its uncommitted Media work.
This extends [18](18-media-generation-plan.md); HTTP provider support does not
satisfy the first-party inference objective.

## Historical recommendation and approval boundary, superseded

The superseded recommendation was a narrow first-party image worker using a pinned stable-diffusion.cpp
C API build, initially Stable Diffusion 1.5 at 512 x 512, one image per operation.
Use Vulkan and CPU builds first for the two target operating systems. CUDA is
an optional later build after its driver, distribution and size costs are
verified. Do not reuse llama-server as a diffusion engine.

The initial model is a conservative integration baseline, not the smallest
published network. Smaller useful candidates and their conversion costs are
listed below. The initial architecture avoids Python, custom nodes and model
repository code. Permit only the approved checkpoint hash and format initially;
additional architectures, quants and companions require explicit capability
entries and native evidence.

The earlier approval covered: engine/build boundary, SD1.5 evaluation model and its
OpenRAIL obligations, explicit download of pinned sources/model for development,
and a bounded worker integration. It does not approve general Python installs,
automatic downloads, public redistribution, release or a claim of hardware fit.
If existing build tools cannot build the selected backend, report the missing
toolchain before installing anything. The earlier direction was, after a successful native image,
assess a smaller distilled candidate and a modern quality candidate. CUDA stays
a measured follow-up against Vulkan on identical hardware/model before choosing
a default. No bundled model redistribution, automatic downloads or new persistent
model format is approved.

Engine tag resolves to exact commit
`2f016e8e30ab0d0051c268944ae96c0951dba4f0` (`git ls-remote`, read-only).
Windows already has CMake and Vulkan SDK 1.4.363.0 tools; the C++ toolchain is
used by existing packaging. WSL exposed a C++ compiler but no CMake/Vulkan tools
on PATH in the initial inventory. This does not establish Linux native feasibility.

## Current branch audit

| Authority or behaviour | Observed implementation and reuse decision |
| --- | --- |
| Media contract | `MediaGenerationRequest`, `IMediaGenerationService`, `GeneratedMedia` and `MediaGenerationResult` cover explicit operations and session bytes. Extend the existing router with one local provider; do not add another media subsystem. |
| Provider adapters | `MediaGenerationService` submits OpenAI-compatible image/audio/video requests and trusted ComfyUI API workflows. It does not load models or own those servers. Retain these optional routes and regression coverage. |
| Operation lifetime | `MediaGenerationViewModel` owns cancellation, rejects late progress/results, retains prior results on failure, and joins active work on shutdown. HTTP cancellation cannot stop remote jobs. A local worker must offer the stronger owned-exit receipt described below. |
| Outputs | Responses are bounded to eight files and 128 MiB combined; control/base64 JSON has separate limits. Signature detection is not full decoding. Native view bounds decoded image dimensions/pixels and preview size, disposes bitmaps, and owns picker callbacks by identity. Reuse preview and `MediaFileService` atomic explicit export. |
| Runtime authority | `ManagedRuntimeRegistry` owns `ServerProcessManager` instances keyed by stable server ID. That manager is coupled to server launch/health/log/telemetry. Do not masquerade a one-shot worker as a llama HTTP server or create a second general runtime registry. Extend the existing ownership infrastructure only as needed for the specialised worker. |
| Process limits | `ArgumentList`, bounded process readers and redacted logs are reusable. `ProcessJobObject` is a process-wide Windows job, assigned after launch; assignment failure currently permits some server starts. Linux has no group/session launcher here. Tree kill is best effort, not guaranteed containment. Neither existing behaviour closes the new worker's acceptance claim. |
| Admission | `ResourceCoordinator`, `IResourceConsumerRegistry`, allocation components and admission leases already model host/device memory, Unknown and concurrent reservations. Register one image consumer, acquire before load, complete its allocation only when loaded, release only after verified exit. Never unload another workload silently. Unknown is not Fits. |
| Models/downloads | `ModelManifestStore` already records repository, revision, SHA256 and companions. `ModelDownloadService` is a transport with a separate verification operation, not automatic safe adoption. Reuse reviewed download/provenance and `ModelPathSafety`; require pinned size/hash and containment before execution. Existing GGUF/llama capability facts do not prove diffusion capability. |
| Hardware/diagnostics | Reuse system resource snapshots, device identity, runtime telemetry and runtime logs. Keep model-specific checks with the image subsystem, with a small typed diagnostic contribution if needed; do not teach the large Doctor/Setup services every tensor architecture. |
| Dependencies/packaging | Rag and Voice already use CPU `Microsoft.ML.OnnxRuntime` 1.25.1. That supplies neither a diffusion pipeline nor CUDA/Vulkan packaging. Media adds no package. Existing package scripts deliver notices and media help; new native payloads need separate exact provenance/notices and archive checks. |
| Tests | Existing Media tests cover provider fixtures, bounds, cancellation/deadlines, picker lifetime, native bitmap preview and explicit export. These are not inference, GPU fit, audible/video playback or owner workflow evidence. |

Settings remain under the existing save/secret authority. A local model selector
should be session-only for the first slice, avoiding a new persisted schema.
No raw prompt, model output, credential or full native log should enter persisted
diagnostics. No Agent capability or new trust/security authority is proposed.

## Engine comparison

| Engine | Actual support and platform evidence | Cost and conclusion |
| --- | --- | --- |
| llama.cpp / GGML | Upstream llama.cpp describes LLM inference and multimodal input. Its reviewed model list does not establish SD/Qwen diffusion execution. GGML supplies numerical operations; GGUF stores tensors and metadata. Neither supplies a scheduler, text conditioning, denoiser and VAE pipeline automatically. | Existing executable cannot be assumed capable. Extending it means implementing and maintaining those architectures. Reuse infrastructure and numerical libraries, not an invented llama image API. Reject for initial slice. |
| stable-diffusion.cpp | Released rolling build `master-970-2f016e8`, dated 2026-10-16, has CPU and Vulkan assets for Windows/Linux, Windows CUDA assets, SD1/2, SDXL, distilled models and Qwen Image 2.1 instructions including the exact Turbo sigma preset. C API exposes context create/free, generation, progress, cancellation and image freeing. | MIT engine, with bundled GGML, tokenizer/Unicode/archive components and backend obligations still needing exact inventory. API/CLI explicitly change frequently. Pin the whole build and ABI. Best match for a small native worker; no Python runtime. |
| ONNX Runtime pipeline | Microsoft's C# tutorial uses exported UNet, CLIP, VAE, tokenizer and scheduler code; GPU builds/providers are separate. Existing CPU ORT packages can run graphs but do not provide this pipeline. No reviewed released Qwen 2.1 Turbo ONNX pipeline was established. | MIT runtime, conversion/precision/operator compatibility and GPU dependency closure remain work. Avoid leaking ORT types from Rag or taking its runtime ownership. Credible alternative for a fixed SD model, more pipeline code than the recommended engine. |
| Controlled PyTorch/Diffusers worker | Qwen's exact Turbo card requires source Diffusers support for saved sampling sigmas and Transformers >=5.17.0. PyTorch supports CPU/CUDA on Windows/Linux; it does not make this a small portable Vulkan solution. LibTorch alone does not supply Diffusers' Python pipeline. | PyTorch BSD-style, Diffusers/Transformers Apache-2.0, plus Python and GPU redistribution obligations. Large environment/ABI/update footprint; exact installed size unmeasured. Broadest fast-moving model support, disproportionate first slice. Defer. |
| OnnxStream | Upstream provides C++ streaming ONNX SD1.5/SDXL examples and C# bindings, with very low memory CPU demonstrations. Its low-memory claims trade latency and precision, and are not measurements on these PCs. | MIT plus XNNPACK/dependency inventory, converted models and a separate execution engine. Useful CPU research alternative; no reviewed Qwen 2.1 Turbo or equivalent Vulkan path. Defer. |
| Axodox MachineLearning | Native ONNX SD1.5/XL pipeline exists, but upstream explicitly says its current code/packages target Windows and DirectML. | MIT; Linux port plus Axodox.Common and ORT dependency closure would expand scope. Reject as initial cross-platform choice. |

Upstream release asset listings, not downloads, showed approximately 16.8 MB
Windows CPU, 28.8 MB Windows Vulkan, 25 MB Linux CPU and 35.3 MB Linux Vulkan.
Windows CUDA was 322 MB with a separately listed 537 MB cudart bundle. These
are compressed upstream archives, not the size of a Hermaeus worker or proof of
its dependencies. Building a library-only worker with WebP/WebM/examples disabled
should reduce unnecessary surface; its actual size and dependency closure must
be measured. CPU/Vulkan availability is not a tested GPU compatibility claim.

## Models and complete memory accounting

The current Windows host reports RTX 4060 Laptop GPU and 16,869,351,424 bytes of
host RAM. GTX 1660 Super 6 GB / Pop!_OS/COSMIC is owner-specified hardware, not
hardware observed in this environment. Neither has run these proposed models.

| Candidate | Components and constraints | Decision |
| --- | --- | --- |
| Qwen/Qwen-Image-2.1-Turbo | Exists, 7B visual generator, eight-step schedule, Qwen3-VL-8B text encoder and its own VAE. Official repository weight files total about 32.44 GB: 14.23 GB transformer, 17.53 GB encoder, 0.676 GB VAE. Quantising only the generator omits most of the problem. KV/prefix cache, activations, transfer buffers and decode add memory. BF16 execution cannot be assumed on the GTX. | Defer initial support. Engine supports a Turbo preset, but a complete quantised/offloaded configuration needs separate native evidence, especially with 16 GB RAM. Qwen Research licence limits non-commercial use to research/evaluation; redistribution requires agreement, modification and attribution notices. Hermaeus's own noncommercial licence does not automatically satisfy it. |
| Stable Diffusion 1.5 | One original `v1-5-pruned-emaonly.safetensors`, 4,265,146,304 bytes, includes denoiser, CLIP and VAE. Separate FP16 component listings total about 2.13 GB, excluding safety checker. The original checkpoint's on-disk size is not resident memory. Quant/offload can reduce weight residency, but VAE/CLIP, activations, scratch, native copies, display/driver use and other Hermaeus consumers still count. | Recommended first native integration baseline at 512 x 512, one image, fixed conservative steps. CPU fallback is technically supported, latency and peak RSS unknown. 6/8 GB are plausible evaluation targets, not validated Fits. Do not enable arbitrary quant variants initially. |
| BK-SDM-Tiny / Segmind Tiny-SD | Smaller six-block SD-family denoisers. Nota's official FP16 safetensors list about 0.647 GB UNet +0.246 GB CLIP +0.167 GB VAE, roughly 1.06 GB before workspaces. Its optional safety checker is another 0.608 GB. Segmind's tiny card names its Realistic Vision base and supplies pickle `.bin` components. | Small useful candidate worth evaluating after baseline. Pinned engine instructions require controlled conversion to a contiguous merged safetensors file. Do not load pickle or repository Python in the app. No conversion tools installed or converted-model provenance verified yet. OpenRAIL restrictions and inherited base obligations apply. |
| SDXS-512 DreamShaper / 0.9 | One-step reduced UNet and tiny autoencoder; distinct variants, different encoder sizes. DreamShaper component safetensors list about 1.765 GB at published precision. Upstream engine documents converted files and mandatory CFG=1/steps=1. | Smaller/faster does not establish useful quality or complete fit. Author/conversion provenance, OpenRAIL++ and inherited DreamShaper terms need review. Defer rather than accept arbitrary community GGUF because its filename looks compatible. |
| FLUX.2-klein-4B | Apache-2.0 publisher candidate; full pipeline includes a separate text encoder and VAE. Publisher's approximate 13 GB VRAM full configuration exceeds the target capacities. | Possible later quant/offload evaluation, not the smallest reliable starting slice. |

SD1.5 uses CreativeML OpenRAIL-M: redistribution must carry the licence and
use restrictions, and modifications/attribution must remain explicit. Its model
card describes an intended safety checker; the proposed native engine is not
proof that checker exists. Do not imply content filtering or silently equate
engine MIT licensing with model permissions. Initially use an explicit developer
download for evaluation; do not bundle model weights in application archives.

Pinned SD1.5 repository revision: `451f4fe16113bff5a5d2269ed5ad43b0592e9a14`.
Checkpoint SHA256: `6ce0161689b3853acaa03779ec93eafe75a02f4ced659bee03f50797806fa2fa`.
Pinned Qwen repository revision: `fc13ba1986fcb905854dc8de2f52eee395f49b5b`.
These are metadata observations, not downloaded-file verification.

Admission must account for full component residency and transient peaks, not
file size alone. Start with conservative host/device reservations, refuse
insufficient or unreviewed Unknown headroom, and record actual native peaks
before relaxing estimates. Do not silently evict chat/voice models. CPU fallback
must be explicitly selected, with its own reservation and deadline, not an
automatic multi-minute surprise after GPU failure.

## Historical proposed vertical slice, not selected for implementation

1. Session-only selection of the exact supported checkpoint and an approved
   pinned backend build. Validate canonical path/reparse ancestry, exact size and
   SHA256, bounded safetensors metadata, architecture, tensor layout and dtype
   before native load. Refuse unsupported components/quantisation, never guess
   from an extension or model name. No implicit network or download.
2. Register one image workload with the existing resource authority. Own one
   operation at a time and an admission lease before loading. Expose capability
   facts and diagnostic failures through existing UI/service contracts.
3. A small first-party worker loads the pinned C ABI outside the desktop process.
   Use length-bounded private standard streams for requests/results; prompts
   never become shell arguments, files or logs. Do not run upstream HTTP/server,
   arbitrary CLI switches, Python, custom nodes or model code. No sockets or
   download features are needed. This is crash isolation, not an OS sandbox.
4. Create/load a context, generate one bounded RGB image, return bounded bytes
   into the existing Media result, free native images/context and exit. Encode
   using existing capabilities where practical. Model residency lasts only for
   that operation, so unload is unambiguous before starting another.
5. Cancellation requests native cancellation, then escalates to owned termination
   after a bounded grace period. Deadline, decoder failure, OOM, malformed IPC
   and crash all join/verify the worker's exit before releasing residency. Never
   publish a late image after cancel or automatically retry an OOM.
6. Extend existing ownership primitives rather than invent a second manager.
   Require Windows job assignment before permitting native work, with a startup
   handshake that prevents work before ownership. On Linux require a verified
   parent-death/pipe-loss exit path and process-group/session handling appropriate
   to the worker. Resolve app-crash and assignment-failure cases in tests before
   activation. If termination cannot be established, report cleanup failure,
   retain the unresolved allocation and refuse a clean-shutdown claim.
7. Reuse the existing image preview and atomic export. No temporary image output
   file, history database, persisted prompt, automatic open or media retention.
   Keep existing ComfyUI/OpenAI paths and their cancellation disclosures intact.

The implementation must not claim these lifecycle properties merely because
the proposal says so. Any material change to this boundary returns to owner
review, particularly a new persistent model format, privilege/sandbox mechanism,
general runtime manager or network server.

## Historical proposed acceptance matrix, not an active work package

Before enabling the local route, contract tests must cover bounded metadata,
bad offsets/overflows, duplicate or unknown architectures/dtypes, missing/corrupt
components, model hash changes, traversal/reparse paths, denied/Unknown admission,
input/output and IPC budgets, pre-load/mid-load/mid-generation cancellation,
deadline, crash, GPU OOM, failed job assignment, broken streams, repeated cleanup,
shutdown with in-flight work and no late publication. Reuse export/preview and
existing provider regressions; deterministic workers do not prove native inference.

Native receipts must name exact engine/submodule build, model hash, driver,
backend, CPU/GPU/RAM, request/seed/resolution/steps, observed host/device peaks,
output decode, export hash and owned-process exit. Run generation/cancel/recovery
repeatedly on Windows and Linux. WSL/Xvfb is not Pop!_OS/COSMIC acceptance. A GPU
cannot be marked supported until actual inference and failure recovery pass on
that target. Owner COSMIC testing remains deferred; original R34 release and
vault activation gates remain intact.

## Primary sources inspected

- [Pinned engine README](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/README.md), [build instructions](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/docs/build.md), [C API](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/include/stable-diffusion.h), [third-party build](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/thirdparty/CMakeLists.txt), [release/assets](https://github.com/leejet/stable-diffusion.cpp/releases/tag/master-970-2f016e8).
- [Pinned Qwen 2.1/Turbo instructions](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/docs/qwen_image_2.1.md), [Qwen publisher card](https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo/tree/fc13ba1986fcb905854dc8de2f52eee395f49b5b), [Qwen licence](https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo/blob/fc13ba1986fcb905854dc8de2f52eee395f49b5b/LICENSE).
- [llama.cpp](https://github.com/ggml-org/llama.cpp), [supported models](https://github.com/ggml-org/llama.cpp/blob/master/docs/models.md), [GGML](https://github.com/ggml-org/ggml).
- [Microsoft C# diffusion tutorial](https://onnxruntime.ai/docs/tutorials/csharp/stable-diffusion-csharp.html), [PyTorch platforms](https://pytorch.org/get-started/locally/), [OnnxStream](https://github.com/vitoplantamura/OnnxStream), [Axodox](https://github.com/axodox/axodox-machinelearning).
- [SD1.5 pinned model](https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5/tree/451f4fe16113bff5a5d2269ed5ad43b0592e9a14), [original licence](https://huggingface.co/spaces/CompVis/stable-diffusion-license/blob/main/license.txt), [BK-SDM-Tiny](https://huggingface.co/nota-ai/bk-sdm-tiny/tree/0364108e53b7f7f4d2585e817a0b7a83dc261cfa), [Tiny-SD](https://huggingface.co/segmind/tiny-sd/tree/cad0bd7495fa6c4bcca01b19a723dc91627fe84f), [SDXS publisher](https://huggingface.co/IDKiro/sdxs-512-dreamshaper/tree/76f720262bb051da75666b22c902a78c8e16c763), [pinned engine conversion instructions](https://github.com/leejet/stable-diffusion.cpp/blob/master-970-2f016e8/docs/distilled_sd.md).

Public metadata/source receipts are retained outside the checkout under
`%TEMP%/hermaeus-media-gate-a827fc3e83c545c5ab8486fa82ecbabb/`.
The unauthenticated GitHub metadata API hit its rate limit; public release HTML
and pinned raw sources supplied the engine evidence. The research phase used
metadata and source only; subsequent authorised development downloads and the
stopped build are recorded above. Exact native package size, dependency inventory,
GPU fit, latency, OOM behaviour and owner acceptance remain unverified.
