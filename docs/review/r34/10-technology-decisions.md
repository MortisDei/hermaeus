# 10. Technology decisions and primary research

Checked 2026-10-16. **No integration, upgrade or new dependency is approved by
research alone.** Decisions refer to the implementation packages in [11](11-dependency-roadmap.md).
Retain/Simplify/Replace are recommendations with their stated gates, not work
already completed. All claimed hardware benefits are Unknown.

## Decision register

| ID | Technology | Disposition and reason | Work or explicit exit |
| --- | --- | --- | --- |
| T01 | Avalonia native controls | **Retain.** Native desktop remains product architecture | D3/U1/G1; no browser frontend |
| T02 | AvaloniaEdit 12.0.0 | **Retain.** Sole editor already shipped; no equivalent replacement need demonstrated | D2/U1 preserve editor behaviour; no Monaco return |
| T03 | Compiled bindings | **Simplify incrementally**, not global flip; typed/dynamic boundaries need actual compilation/native checks | D2, F01 |
| T04 | Custom tooltip | **Replace conditionally.** Upstream fix exists at selected framework source; native equivalence not yet earned | D3, F03; retain overlay until both platform gates pass |
| T05 | Wheel helper | **Retain.** Nested edge bubbling/horizontal policy is real behaviour | D3 comparison only; delete only if whole contract proven equivalent |
| T06 | Modal placement | **Retain.** Owner timing/DPI/clamp needs native proof before replacement | D3/U1 |
| T07 | MarkdownViewer/Markdig | **Retain.** Streaming, selection and lifecycle functionality outweigh unproven generic-control replacement | P1 measured tuning only |
| T08 | Inter package | **Simplify conditionally.** Active startup registration conflicts with no-bundled-brand-font direction; native role defaults already exist | D1, DEL-01 |
| T09 | DI composition/explicit refs | **Retain Composition; simplify redundant Desktop declaration if proven.** Same version does not mean two service containers | D1; no new DI abstraction |
| T10 | SQLite security pin | **Retain.** Preserve patched native dependency and final-version proof | B0/G1; no unreviewed pin removal |
| T11 | ONNX in Voice/RAG | **Retain.** Distinct model responsibilities, common package version | B0/P1/L2 profile sessions, no automatic deduplication |
| T12 | PdfPig/Tokenizers/Tensors/DBus | **Retain functional dependencies; simplify direct DBus pin conditionally** | D1/P1/S2; exact usage in [03](03-dependencies-and-native-assets.md) |
| T13 | Toolkit navigation notifications | **Simplify** generated dependents, retain side-effect hook and cross-VM updates | D2, F02; no guaranteed large line reduction |
| T14 | Recall exact scan | **Retain reference, simplify measured allocation hot path** | P1/P2; exact relevance/scope/fallback acceptance |
| T15 | Vector compression/pruning | **Defer adoption until measured.** Storage codecs and approximate retrieval have migration/quality cost | P2 experiment; no default lexical prefilter |
| T16 | SQLite vector extension | **Defer dependency decision.** Existing SQLite may be enough after simpler work | P2/E04; owner approval, native/RID/security proof before adoption |
| T17 | Embedding alternatives | **Conditional comparison**, retain Qwen baseline; text-only compatible Gemma candidate first | P2/E01-E03, no multimodal expansion or default switch |
| T18 | llama.cpp idle sleep | **Conditional experiment**, before a router | P3/R03; actual released memory/admission/wake required |
| T19 | llama.cpp router | **Defer integration.** Child lifecycle/autoload must fit existing Services/admission authority | P3 research; no second config/runtime owner |
| T20 | KV quantisation and Vulkan SFA | **Conditional measurement**, exact runtime/model/backend and quality proof | P3/R03/R05; never assume target-GPU support |
| T21 | PTQ1_0/ternary and other low-bit research | **Defer adoption.** Publisher-specific transforms/builds require explicit compatibility and trust review | P3/R04; no auto-fork or model conversion |
| T22 | Microsoft Execution Containers | **Defer production adoption.** Backend-specific policy/OS/packaging proof needed despite GA announcement | S4/R01; owner boundary/dependency decision |
| T23 | NVIDIA OpenShell | **Defer integration.** Gateway, policy, credentials and deployment machinery exceed a simple execution wrapper | S4/R02; existing Hermaeus approval remains sole action authority |
| T24 | NVIDIA PAIR | **Reject managed integration in R34.** Routes independent inference requests to nodes; does not pool VRAM or split one model/request | R06; optional future external provider compatibility needs a separate request, no discovery/scheduler/network expansion |
| T25 | Proof-of-Control | **Defer adoption/conformance claim.** Draft reference for evidence-boundary review, not a replacement approval/gate or proof existing local receipts resist a hostile OS user | R07; S4 may use adversarial ideas only, no signed ledger/cryptographic subsystem |
| T26 | vLLM persistent weights | **Defer runtime/dependency adoption.** Learn from residency trade-offs; persistent GPU allocations do not establish memory release | R08; no daemon, Python server or runtime manager added |
| T27 | JetBrains Air/Boro | **Air: interaction research only; Boro: defer, identity Unknown.** No verified primary source matched the Boro name | R09; U1 may inspect review/task affordances. No IDE/ACP dependency or orchestration import |
| T28 | Jan AI/AnythingLLM/Open WebUI | **Retain as interaction references only** | U1/U01-U03; no architecture replacement, product defaults or frontend reuse |
| T29 | Moss refinement | **Retain native brand, targeted polish after captures** | U1; no new fonts, mascot ornament or themed framework |
| T30 | Avalonia 12.1.4 | **Defer bump until bounded compatibility/native gate.** New release has relevant control/lifetime fixes, not proof of a Hermaeus leak or automatic permission to upgrade | D3/F04; retain separate framework/editor pins until tested |
| T31 | .NET AEAD/streams/process primitives | **Retain framework capability before dependencies** | S1-S3; built-in APIs suffice for bounded readers and authenticated format, F05 |
| T32 | Another authority or feature programme | **Reject.** Distributed inference, multi-user Agent API, another approval/lifecycle system, new vector DB, browser frontend, replacement editor and automatic model routing are outside R34 | No work package; reconsider only with a new owner-approved premise |

## Approved execution refinements, 2026-10-16

- **B0/B1:** minimum reproducible source/build/security evidence unlocks safety
  repairs and lifecycle reproduction. Per-host performance/native/package
  evidence gates optimisation claims and release, not the start of S1/S2/L1.
- **S1:** investigate Windows Job Objects and Linux process groups/sessions,
  including start/assignment races, inherited descendants and escape. Tree-kill
  is not guaranteed containment. No new security boundary is approved.
- **S2:** owner-approved ceilings are 512 MiB local source, 16 Mi extracted
  characters, 2,000 PDF pages and 256 MiB candidate text/vector payload, following
  legitimate PDF measurements in [13](13-execution-and-evidence.md). Preserve
  the existing web limit; further user-visible limit changes need approval.
- **S3/T31:** isolate migration and commit; retain the current persisted format
  until owner approval of exact format, atomic failure/recovery and downgrade
  behaviour. Document technical irreversibility rather than promising rollback.
- **G1:** conditional choices can close by evidence-backed Retain/Defer without
  implementing an unnecessary change. Required safety, native acceptance,
  packaging notice delivery and unresolved asset licensing remain release gates.

## Framework sources

| Ref | Primary source and observed claim | Limitation |
| --- | --- | --- |
| F01 | [Avalonia compiled bindings](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings): v12 defaults compiled; explicit typing and per-scope/reflection controls exist | Mutable docs; use pinned 12.1.3 compiler in D2, no measured Hermaeus gain |
| F02 | [Toolkit ObservableProperty](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty): dependent notification attributes and partial change hooks | Current docs checked against field-based 8.3.2 pattern already used in source; build/tests must prove generated ordering |
| F03 | [Issue #19218](https://github.com/AvaloniaUI/Avalonia/issues/19218), [fix PR #21965](https://github.com/AvaloniaUI/Avalonia/pull/21965), [12.1.3 ToolTipService source](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/ToolTipService.cs) | Read-only GitHub API confirmed closed 2026-08-13, PR merged as `11c542726898ae954a1ef668c65ec79ec92ab17d`, exact tag contains pending-close/background logic. Merge ancestry comparison diverges because branches differ; source content, not ancestry, establishes this fact. Native Hermaeus equivalence remains Unknown |
| F04 | [Avalonia 12.1.4 release](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4), published 2026-10-09T13:46:01Z | GitHub API confirms release and listed text-layout, detached-visual, command/style lifetime and platform fixes; no Hermaeus adoption or platform pass |
| F05 | [.NET 10 AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0) | Built-in authenticated encryption availability is not a safe migration design; runtime support, nonce/tag/key/atomicity still require tests |

## Runtime and containment sources

Sources with SHA paths below are immutable snapshots read through GitHub or its
API on the planning date. Other pages are dated mutable documentation. Freeze
the exact release/binary/asset revision again before implementation experiments.

### R01: Microsoft Execution Containers

[Microsoft announcement, 2026-10-07](https://blogs.windows.com/windowsdeveloper/2026/10/07/microsoft-execution-containers-policy-driven-containment-for-ai-agents/)
describes GA containment and differentiates enforcement, denied-access learning
and permissive observation. An older Microsoft integration tutorial still calls
MXC early preview; that is not a current blanket status claim.

Pinned repository head: `ba21bb61a42d20a7525ae8a9cd77ce2564cbb631`
(commit timestamp 2026-10-15T21:20:52Z). The
[README](https://github.com/microsoft/mxc/blob/ba21bb61a42d20a7525ae8a9cd77ce2564cbb631/README.md)
describes .NET/native SDK assets and multiple platform backends, some experimental.
The [OS support table](https://github.com/microsoft/mxc/blob/ba21bb61a42d20a7525ae8a9cd77ce2564cbb631/docs/backends/process-container/os-version-support.md)
requires exact Windows servicing builds/capability probing, not just Windows 11.
[Process networking](https://github.com/microsoft/mxc/blob/ba21bb61a42d20a7525ae8a9cd77ce2564cbb631/docs/backends/process-container/networking.md)
documents backend-specific directional networking and capability constraints.
[Bubblewrap](https://github.com/microsoft/mxc/blob/ba21bb61a42d20a7525ae8a9cd77ce2564cbb631/docs/backends/bwrap/bubblewrap-backend.md)
requires user namespaces/bwrap and additional tools for network-enabled modes,
with unsupported policy refused. Hermaeus compatibility/overhead is Unknown.

### R02: NVIDIA OpenShell

Pinned head `6546691d5e493343c7877573252e835d28bf9e23`
(2026-10-15T19:09:14Z). The
[README](https://github.com/NVIDIA/OpenShell/blob/6546691d5e493343c7877573252e835d28bf9e23/README.md)
describes 0.1.x, Linux/macOS and experimental Windows WSL2 deployment with a
gateway and container/VM prerequisite. Read the exact released support matrix
before any adoption; native Windows proposals are not parity evidence.

[Security practices](https://github.com/NVIDIA/OpenShell/blob/6546691d5e493343c7877573252e835d28bf9e23/docs/security/best-practices.mdx)
distinguish mandatory Landlock ABI 3 baseline from extra best-effort filesystem
policy. Extra policy can require hard enforcement; L7 audit can forward denied
actions and policy approvals can durably widen endpoint access. These are
production-reliance blockers unless configured/proven appropriately beneath
Hermaeus's authority. No installation, kernel enforcement or overhead was tested.

### R03: llama.cpp server and ownership

[Server documentation](https://github.com/ggml-org/llama.cpp/blob/2f729cfa4ca6f55b8bacf67bb2ba92cb9548e291/tools/server/README.md)
and [developer documentation](https://github.com/ggml-org/llama.cpp/blob/2f729cfa4ca6f55b8bacf67bb2ba92cb9548e291/tools/server/README-dev.md),
master at `2f729cfa4ca6f55b8bacf67bb2ba92cb9548e291`
(2026-10-15T21:53:02Z). These provide the advertisement and child/sleep lifecycle
facts used in [05](05-runtime-and-inference-research.md). Do not equate master
with b10034, b10930, b11491 or a release. Installed/effective behaviour is Unknown.

### R04: PTQ1_0 and ternary research

Publisher [Bonsai backend/format support](https://github.com/PrismML-Eng/Bonsai-demo/blob/main/BACKEND-SUPPORT.md)
names demo baseline `prism-b10772-e8fd209`, distinguishes released/source/native
backend evidence and says Bonsai 2 formats/transforms require its fork pending
upstream integration. File recognition/loading alone can omit required
transforms. This is publisher compatibility documentation, not independently
verified Hermaeus quality, fit or throughput. No performance number is imported.

### R05: released llama.cpp capability delta

[v0.6.0](https://github.com/ggml-org/llama.cpp/releases/tag/v0.6.0), published
2026-10-05T16:56:22Z. GitHub tag dereference confirmed commit
`d81235049384534c167caea52b85a694f6103d14`; release `target_commitish` is not
used as the tagged source SHA. Vulkan quantised sparse-attention support is an
upstream release claim. Hermaeus's b-number selection and target hardware still
require P3 investigation; no automatic update or speed claim follows.

### R06: NVIDIA PAIR

[NVIDIA project](https://github.com/NVIDIA/Personal-AI-Router) and
[architecture](https://docs.nvidia.com/local-ai/nvpair/architecture/) describe
request routing/scheduling among machines with independent engine/model copies.
They explicitly do not pool VRAM or split an in-flight request/model. R34 has no
distributed workload or justification for another control plane.

### R07: Proof-of-Control

[Publisher draft](https://advancedaisociety.org/proof-of-control) describes
action-boundary evidence and public comment through 2026-10-30; its reference
implementation is not presented for production deployment. Use it as research
for adversarial evidence questions. Do not claim standard conformance,
independent cryptographic verification or hostile-host tamper resistance for
Hermaeus's existing receipts.

### R08: vLLM residency research

[Preload documentation](https://docs.vllm.ai/en/latest/features/preload/)
describes daemon-held GPU weights and CUDA IPC across engine restarts, with
zero-copy and copy trade-offs and a zero-copy/sleep incompatibility.
[v0.31.0 sleep documentation](https://docs.vllm.ai/en/v0.31.0/features/sleep_mode/)
separates CPU offload from discard/reload. These are research analogues;
persistent weights, sleeping state and reclaimed VRAM are different claims.
No vLLM process or dependency is selected.

### R09: JetBrains Air and unresolved Boro identity

[JetBrains Air](https://www.jetbrains.com/air/) describes agent sessions and
review in development environments. Inspectable task/diff review is relevant
interaction research. Searches for the exact Boro/JetBrains pairing returned
no verifiable primary product/specification in this pass. Record **Unknown**,
do not invent a codename, capability or dependency. This does not block R34;
revisit only with a primary source and a concrete need.

## Embedding and retrieval sources

| Ref | Primary evidence, checked on planning date | R34 limit |
| --- | --- | --- |
| E01 | [Qwen3 0.6B model card](https://huggingface.co/Qwen/Qwen3-Embedding-0.6B): text/instruction-aware family and dimensions; repository-pinned Doctor artifact remains the local baseline | No publisher leaderboard score is a Hermaeus eval pass |
| E02 | [Google EmbeddingGemma card](https://ai.google.dev/gemma/docs/embeddinggemma/model_card) and [ggml-org 300M GGUF](https://huggingface.co/ggml-org/embeddinggemma-300M-GGUF): text model and available GGUF usage | Exact runtime/artifact/licence/quality compatibility untested here |
| E03 | [Google EmbeddingGemma 2 card](https://ai.google.dev/gemma/docs/embeddinggemma/model_card_2?hl=en): newer multimodal model including text | No compatible Hermaeus artefact or execution proved; text-only evaluation conditional, multimodal expansion rejected |
| E04 | [sqlite-vec maintainer documentation](https://alexgarcia.xyz/sqlite-vec/): an available SQLite extension approach | No dependency selected; native loading, security pin, quality, packaging and migration unverified |

## Interaction sources

| Ref | Primary source | Permitted use |
| --- | --- | --- |
| U01 | [Jan documentation](https://www.jan.ai/docs) | Model/service discoverability ideas only |
| U02 | [AnythingLLM document workflow](https://docs.anythingllm.com/chatting-with-documents/introduction) | Conversation attachments versus shared embedded knowledge disclosure |
| U03 | [Open WebUI model workspace](https://docs.openwebui.com/features/workspace/models/) | Explicit capability/source affordances, no architecture/default imports |

Research limitations: mutable documentation can move; selected GitHub pages
failed browser retrieval and were verified read-only through `gh api` instead.
No vendor tool, sandbox, model or comparative UI was executed. Freeze sources
again when implementing a capability; date and reference alone cannot close
runtime, security or owner acceptance.
