# Technical Debt Register (2026-07)

Ordered by expected pain if unaddressed. Items reference documents 01/03 where
the remediation is designed.

## 1. God services will absorb all future change

`DoctorService`, `BenchmarkService`, `LocalAiSetupService` (~45 KB each) plus
the three big ViewModels (`ChatViewModel`, `AgentViewModel`, `RagViewModel`).
These are where merge conflicts, regressions, and "afraid to touch it" live.
Doctor and Setup encode cross-cutting knowledge of every subsystem — the
maintenance trap is that adding *any* feature means editing them, so they grow
monotonically. Remediation: check/fix registry (Opportunities #5) — **DONE**,
Doctor/Trust/Privacy Audit are now `IInspectionCheckProvider` contributors
over one `IInspectionEngine`. `BenchmarkService` and the three big
ViewModels' non-inspection orchestration are still unaddressed (see
Evaluation System, doc 10, and Review §5).

## 2. Duplicated concepts (the biggest hidden complexity)

- **Three memories:** chat memory, workspace memory, workspace profiles —
  **DONE**, unified onto one scoped `IMemoryStore`.
- **Four inspectors:** Doctor, Setup scan, Trust & Safety, Privacy Audit —
  **DONE**, one inspection engine with filtered views.
- **Three trace shapes:** chat traces, RAG traces, agent JSONL traces —
  **DONE**, one `TraceRecord` schema with per-surface projections.
- **Three context packers:** ChatViewModel, Agent context packs, RagPipeline —
  **DONE**, one `ContextPackBuilder` used by all three.
- **Two model-comparison systems:** Compare Models and Benchmarks — **DONE,
  scoped**; see the Evaluation System design (doc 10). Both, plus the RAG
  eval harness, now write through the shared `IEvalStore`/`EvalRun` shape and
  a shared atomic-file-write helper. The panels still read from their own
  richer, system-specific stores since nothing yet reads `EvalRun`s back out
  of the shared store; that is new feature work (a shared history/compare
  reader), not remaining duplication.

Each duplication is invisible to users today and becomes a consistency bug
factory tomorrow (e.g., redaction applied to one trace format but not
another). This is the debt to pay down before 2.0, and most of it *reduces*
code. All five collisions here are resolved to the extent a refactor safely
can; the Evaluation System's remaining gap is a reader UI, not duplication.

## 3. Interface ceremony without seams

~25 single-implementation interfaces in `Aether.Core/Services`. Cost: every
change is two edits; readers must chase indirection; the interfaces imply a
pluggability that doesn't exist. Keep genuinely polymorphic ones
(`ILlmService`, `IVoiceProvider`, secret-store backends, doctor checks once
they exist); collapse the rest. Low urgency, high cumulative friction.

## 4. Provider knowledge smeared across layers

Provider tag strings (`"openai"`, `"llama.cpp"`, `"ollama"`) and per-provider
booleans in settings are consumed by Composite, Services UI, Doctor, and
Privacy Audit independently. Any provider addition or rename is a shotgun
change. Remediation: Opportunities #1 — **DONE** for Privacy Audit
(`ProviderDescriptor.IsRemote`). **DONE, scoped** for the two routing
tag-switches that were pure code-organization duplication, with no settings
schema involved: `CompositeLlmService.StreamChatAsync` routed on a
hand-written `tag switch` calling one of three concrete services; it now
looks up a `Dictionary<string, StreamChatDelegate>` built once in the
constructor and keyed by each provider's own `ProviderDescriptor.Tag`, so
adding a provider means adding one dictionary entry, not a new switch arm.
`ServicesViewModel.RuntimeProfileViewModel.KindLabel` duplicated the same
three display strings in its own switch on the unrelated `RuntimeKind` enum;
it now reads `CompositeLlmService.DescriptorFor(kind).DisplayName`, one
source of truth for the label. Still open, deliberately: `PrivacyAuditService.IsChatProviderEnabled`'s
tag switch and `CompositeLlmService.GetModelsAsync`/`IsConfigured`'s
per-provider `if`s bridge to a flat, per-provider settings shape
(`Llm.LlamaCppEnabled`, `Llm.OpenAiEnabled`, `RuntimeProfiles`) that isn't
keyed by tag. Collapsing that means changing the settings schema (and
migrating existing `settings.json` files), which is a materially bigger and
riskier change than a routing-table extraction — left for a dedicated
settings-migration effort, not bundled into this cleanup.

## 5. Leaky abstractions on `ILlmService` — DONE

- `PullModelAsync`/`DeleteModelAsync` on the base interface (Ollama-only) —
  not present in the current interface; no pull/delete functionality exists
  anywhere in the app today (Ollama models must be managed via its own CLI).
- Two streaming methods with a default-interface-method shim — not present;
  `StreamChatAsync` is the only streaming method.
- `temperature` as the only sampling parameter baked into the signature —
  not present; `LlmChatOptions` is already an extensible options record.

## 6. The Python voice stack

A shadow runtime dependency (Python 3.11 *and* 3.12 depending on provider),
venv creation, GPU-backend detection, generated scripts, embedded `.py`
resources, and a `PythonHealthValidator` that exists to fight this fire.
Every OS update, pip resolver change, and CUDA release is a support ticket.
Documented remediation in Dependency Review (ONNX-first voice).

## 7. Naming and vocabulary drift

- "Memory" means three things (see #2). "Workspace" means the agent's root
  *and* the app generally ("Agent workspace", "AI workspace").
- "Profiles": model profiles, runtime profiles, workspace profiles, tune
  profiles — four unrelated schemas sharing a word.
- `Models/` vs `models/` folder ambiguity is handled by heuristic ("prefers
  the folder containing GGUF files") — a smell standing in for a decision.
- Settings sections vs. services don't map 1:1 (`LlmSettings` read by many).

Fix vocabulary in docs and types at the same time as the unifications; naming
debt compounds through every new contributor.

## 8. Custom test harness

`Program.cs` runner + xunit shim means standard tooling (`dotnet test`, IDE
runners, CI matrix, coverage) doesn't work out of the box. Also correlates
with the thinnest coverage being exactly where it matters most: risk
classifier, path-boundary enforcement, redaction regexes, migration runner.
These are security-load-bearing and regex/path logic is where platform quirks
(Windows case-insensitivity, UNC paths, symlinks) breed CVEs.

## 9. Unbounded growth surfaces

Benchmark history, chat/RAG/agent traces, runtime log archives, per-GGUF tune
profiles, memory rows with auto-extraction enabled. Each is small; together
they make the data root a landfill after a year of use. One retention policy
mechanism, applied everywhere, before 1.0.

## 10. Docs describe intent as fact in places

`features.md` contains "should" statements (Doctor Fixes queue, safe command
recipe cards) inside feature documentation, and README front-matter version
(`0.9.4-alpha`) trails `Directory.Build.props` (`0.9.16`). Minor, but for a
project whose brand is trustworthiness, docs that overstate or trail reality
are on-brand damage. Add a doc-truth pass to the release checklist and derive
the README version or drop it.

## 11. `Aether.Core` purity erosion

Core carries `CommunityToolkit.Mvvm` — UI-framework-adjacent machinery inside
the contract layer — **DONE**, removed; an architecture test now enforces
`Aether.Core` stays BCL-only. **DONE**: `Aether.Agent` no longer references
`Aether.Rag`. `AgentModels.cs`'s `AgentRagRetrievalResult` was dead code
(never constructed anywhere) and was deleted outright.
`AgentContextBuilder.cs` now depends on `Aether.Core.Services.IAgentRetrievalService`
(a two-method seam: does a dataset exist, retrieve scored chunks for a
query), implemented by `Aether.Rag.AgentRetrievalService` and wired in DI.
This also deleted the dead, never-implemented `IRagService`/`RagResult`/`RagSource`
that previously sat unused in Core. `Aether.Agent.csproj` no longer
references `Aether.Rag.csproj`; an architecture test
(`Agent_does_not_reference_Rag`) enforces it stays that way.
