# Architecture Review (2026-07, pre-1.0)

Scope: full source review of `src/` (~26k lines C#/AXAML across 6 projects),
tests (~3.5k lines), docs, and dependency graph. Written from the perspective
of maintaining Aether for the next ten years.

## Summary Verdict

The architecture is fundamentally sound. The project layering is clean, the
dependency set is remarkably small for the feature surface, and the
local-first/security posture is consistently applied rather than bolted on.
The real risks are not structural rot; they are (a) a small number of god
classes that will absorb every future feature, (b) three separate memory/state
concepts that have not been unified, and (c) an interface layer in Core that
is one-implementation-per-interface ceremony rather than a real seam.

## Strengths

1. **Layering is real, not decorative.** `Core` (models + interfaces) →
   `Rag`/`Agent`/`Services` → `ViewModels` → `Desktop`. UI code cannot reach
   into storage directly; ViewModels are Avalonia-free (CommunityToolkit only).
   This is the single most valuable property of the codebase. Protect it.
2. **Dependency discipline.** Seven meaningful NuGet packages for an app with
   chat, RAG, ONNX reranking, TTS, process management, and benchmarking. Most
   competitors of this scope carry 50+.
3. **Storage is boring in the best way.** SQLite everywhere, a shared
   versioned migration runner, atomic writes, explicit backup/restore with
   traversal rejection. `task_state.json` as source of truth with a SQLite
   index for listing is a correct and honest pattern.
4. **Safety model of the Agent is explicit and testable.** Deterministic risk
   classification, baseHash stale-file protection, JSONL traces, and a
   documented read-first contract. This is a genuine differentiator; almost
   nobody ships an auditable agent.
5. **Zero-warning builds enforced** (`TreatWarningsAsErrors`), consistent
   nullable-enabled net10.0 across all projects.

## Weaknesses (ranked by impact)

### 1. God services (HIGH)

`DoctorService` (45 KB), `BenchmarkService` (45 KB), `LocalAiSetupService`
(43 KB), `ChatViewModel` (37 KB), `AgentViewModel` (35 KB), `RagViewModel`
(34 KB). These six files are where every new feature lands and where every
regression will originate. Doctor and LocalAiSetup are the worst offenders
because they hard-code knowledge about every subsystem (storage, runtimes,
voice, RAG, GPU, secrets, Python, embedding models). Every new subsystem means
editing Doctor. That is an open/closed violation that will compound.

**Direction:** invert Doctor into a check registry: each subsystem contributes
`IDoctorCheck` (and optionally `IDoctorFix`) instances; DoctorService becomes
a small runner + reporter. Same pattern for setup steps and privacy-audit
contributors. This is the highest-leverage refactor available before 1.0.

### 2. Three memory systems, no unifying concept (HIGH)

- Chat memories (`MemoryStore`, categories, injection, extraction)
- Workspace memory (Agent, per-root notes)
- Workspace profiles (per-root analysis results)

These are three stores, three schemas, three UIs for one user-facing idea:
"what Aether knows persistently." Users will not understand why a fact learned
in chat is invisible to the agent working in the same project. Before 2.0 this
should converge on a single memory service with **scopes** (global / workspace
/ conversation) and one retrieval path, even if the storage stays split
underneath.

### 3. Provider dispatch is enum + if-chains, not a capability model (MEDIUM-HIGH)

`CompositeLlmService` hard-wires exactly three providers (llama.cpp, Ollama,
OpenAI-compatible) with string provider tags and per-provider settings flags
(`Llm.LlamaCppEnabled`, `Llm.OpenAiEnabled`). Adding a fourth backend touches
CompositeLlmService, SettingsService, RuntimeProfileService, ServicesViewModel,
and Doctor. `ILlmService.PullModelAsync`/`DeleteModelAsync` on the base
interface is a leak: only Ollama can pull; others must stub it. See
document 03 for the recommended capability-based provider registry.

### 4. Interface ceremony in Core (MEDIUM)

25 `I*` interfaces, nearly all with exactly one implementation and no test
doubles in sight (the test harness is mostly integration-style). Interfaces
that exist "because DI" are cost without benefit: every signature change is a
two-file edit and the interface tells you nothing the class doesn't.
Keep interfaces where there genuinely are or will be multiple implementations
(`ILlmService`, `IVoiceProvider`, `ISecretStore` backends) and collapse the
rest to concrete classes. DI works fine with concrete registrations.

### 5. ViewModel breadth = hidden orchestration layer (MEDIUM) — partially DONE

`ChatViewModel`, `AgentViewModel`, and `RagViewModel` each orchestrate
multi-service workflows (context packing, attachment reading, memory
injection, trace recording). That logic is untestable without UI plumbing and
unavailable to any future non-UI surface (CLI, automation, local API). Extract
the orchestration into plain services (`ChatSessionService` /
`ContextPackBuilder`) and leave ViewModels as thin bindable adapters. This
matters strategically: the context-pack builder is the heart of the product
and it currently lives inside a ViewModel.

**Extracted, in five slices (2026-07):**
- `ChatContextUsageCalculator` (Core): context-window resolution and usage
  label/percent/warning-level math, previously inline in `ChatViewModel`.
- `RagStreamProtocol` (Rag): the `__RAG_SOURCES__`/`__RAG_TRACE__` sentinel
  parser `RagViewModel` and `RagEvalService` each implemented independently.
- `WorkspaceActivationSelection` (Agent): the preferred-model/dataset id
  lookup `ChatViewModel` and `AgentViewModel` each duplicated.
- `RagDatasetHealthService` (Rag): the per-dataset source/duplicate/missing/
  stale-file computation from `RagViewModel.RefreshDatasetManagerAsync`.
- `ChatTraceService` (Services): chat trace persistence/reload, previously
  `ChatViewModel`'s direct `ITraceStore` calls plus DetailJson mapping.
- `AgentPatchReviewService` (Agent): the apply/reject/block status-transition
  and audit sequence from `AgentViewModel`'s three patch-decision commands.
- `ChatConversationBuilder.AutoTitleFrom` (Core): conversation auto-titling
  from `ChatViewModel.PersistAsync`.

All seven are independently unit-tested without any UI plumbing, closing the
"untestable without UI plumbing" complaint for the pieces moved so far.

**Explicitly not done, and materially higher risk/effort than the above:**
`ChatViewModel.SendAsync` (the live streaming send loop itself),
`RagViewModel.IngestAsync` (the suspend-competing-services/ingest/restore
sequence), and `ChatViewModel.CompareSelectedModelsAsync`. These are the
largest remaining orchestration blobs, but extracting them safely needs
either new integration-test coverage for streaming/cancellation behavior
first (today's coverage is integration-style, not unit-level, for exactly
this code) or accepting real regression risk on the three most-used
surfaces in the app. Revisit as a deliberate, separately-scoped pass rather
than folding into this one.

### 6. Settings monolith (MEDIUM) — evaluated, REJECT full narrowing

`AppSettings` aggregates 8+ domain sections with one save flow. The sectioning
is good; the risk is that every service takes `ISettingsService` and reads
whatever it wants, creating invisible coupling ("who reads
`Llm.OpenAiEnabled`?" is unanswerable without grep). Consider handing each
service only its section. Not urgent, but stop the drift now.

**Evaluated as part of the 2026-07 pass.** `ISettingsService` is referenced
from 93 files in `Aether.Services`/`Rag`/`Agent`/`Voice`/`LocalApi` and 30
more in `Aether.ViewModels`. Narrowing every one of those to a per-section
type would touch well over 100 files, and the underlying storage model works
against a clean split: settings persist as one atomic `settings.json` write
(`SaveAsync`), data-root migration reasons about the whole tree, and
`SettingsChanged` fires once for the whole object, so any per-section
wrapper would still need to hold a reference to the full `AppSettings` for
save/migrate/notify purposes, making the "narrowing" partly cosmetic at the
call sites that matter most. No bug or reported confusion traces back to
this; it is a hypothetical discoverability concern, not an active one. Given
that, and that the interface-collapse pass (docs/review/06-technical-debt.md
item 3) already reduced indirection elsewhere this release, a mechanical
rewrite of 100+ call sites for a "not urgent" item is disproportionate.
**Going forward:** new services should still take only the settings section
they need as a constructor parameter where the type shape allows it (several
already do, e.g. `RedactionService` needs no settings at all); this is a
code-review guideline, not a retrofit of existing services.

### 7. Test harness is custom and thin (MEDIUM)

~3.5k lines, custom `Program.cs` runner plus an xunit-harness shim, mostly
integration-style. For a codebase this security-sensitive, the risk
classifier, path-boundary rules, redaction patterns, patch baseHash logic, and
migration runner deserve dense, fast unit coverage. The custom runner is also
a contributor-hostile choice: `dotnet test` not working the normal way is
friction for every future collaborator. Owning infrastructure is the project
philosophy, but a test runner is not differentiating infrastructure.

### 8. Scalability concerns (LOW-MEDIUM, monitor)

- RAG query cache bounding is done; but SQLite vector search is brute-force
  in-memory — fine to ~100k chunks, plan a stance (not necessarily a fix) for
  beyond that. Do not adopt a vector DB; an on-disk flat index with SIMD
  (`System.Numerics.Tensors` already referenced) will carry a long way.
- Conversation store + FTS is fine indefinitely.
- Per-GGUF tune profiles, benchmark history, trace files grow unboundedly;
  add retention policies before 1.0.

## Unnecessary complexity observed

- Dual streaming APIs on `ILlmService` (`StreamChatAsync` +
  `StreamChatEventsAsync` with a default-interface-method bridge). Pick the
  event-based one, delete the string one before 1.0 — this is exactly the kind
  of compatibility shim that fossilizes.
- Voice provider matrix (Kokoro, F5-TTS, XTTS v2, OpenAI) each with process
  managers, Python health validation, per-provider setup scripts. This is the
  largest complexity-per-user-value area in the app (see Feature Audit).
- Doctor/Setup/Trust/PrivacyAudit are four overlapping "inspect my install"
  systems. They share no check model today.

## What is *not* wrong

Resist the temptation to: introduce a plugin system (nothing needs one yet),
adopt MediatR/Prism/ReactiveUI-style frameworks, split into more projects, or
abstract SQLite behind a repository layer. The current directness is a
strength.
