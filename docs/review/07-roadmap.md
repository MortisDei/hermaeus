# Roadmap (architecture-first)

Principle: 1.0 ships what exists, hardened. 1.x pays structural debt while the
surface is still small. 2.0 is composition, not features. Features that appear
below exist only because an architectural change makes them nearly free.

## Immediate — 1.0 (ship what exists, harden it) — DONE

Goal: a release you can stand behind for years. No new subsystems. This
checklist is complete as of 0.9.20; see docs/review/05-feature-audit.md and
docs/review/06-technical-debt.md for the itemized detail behind each line.

- **Freeze feature growth.** Everything in Feature Audit rated Essential
  ships. Merge/Deprecate/Over-engineered items were revisited rather than
  left frozen: Local tasks/reminders/automations (Deprecate) and the memory
  encryption toggle (Over-engineered, turned out to be a phantom setting)
  were removed outright; the Benchmarking suite (Over-engineered) had its
  duplicate zip-export path and ranking-mode picker cut; the Workspace file
  browser panel (Merge) and Session Usage panel (Merge, done earlier) were
  audited and resolved. See the CHANGELOG "Removed" entries for each.
- **Break the `ILlmService` contract now** — DONE, already satisfied: no
  pull/delete leak on the base interface, one streaming method, `LlmChatOptions`
  is already an extensible options record (docs/review/06-technical-debt.md
  item 5).
- Retention policies for traces, logs, benchmark history, tune profiles —
  DONE. Traces cap at 500 rows/kind, logs keep the newest 10 archives,
  benchmark history keeps the newest 200 runs; tune profiles now prune
  entries for deleted/replaced models and cap at 200, closing the last
  unbounded-growth surface (docs/review/06-technical-debt.md item 9).
- Unit-test the security-load-bearing code: risk classifier, workspace path
  boundaries, redaction patterns, backup extraction guards, migration runner
  — DONE, already covered (`ArchitectureTests.cs`, `ServiceTests.cs`,
  `MigrationRunnerTests.cs`, `Program.cs`). Standard `dotnet test` already
  adopted.
- Architecture tests: ViewModels never reference Avalonia; ONNX Runtime never
  escapes `Aether.Rag`; `Aether.Agent` never references `Aether.Rag` — DONE,
  all three exist in `ArchitectureTests.cs`.
- Docs-truth pass — DONE. `docs/features.md`'s Doctor Fixes queue language
  already correctly hedges it as planned/tracked, not shipped; the real
  drift was `Directory.Build.props` trailing the CHANGELOG's staged version,
  fixed by the 0.9.20 bump.
- Pin Avalonia; document the upgrade playbook — DONE. All five Avalonia
  packages pinned to the same exact `11.3.0`; see docs/avalonia-upgrade.md.

## Near-term — 1.x (pay the four debts)

Each is independently shippable and each *deletes* code:

1. **Check/fix registry** — DONE. Doctor, Trust & Safety, and Privacy Audit
   contribute `IInspectionCheckProvider` checks into one `IInspectionEngine`,
   filtered by view. Setup Wizard already delegated to `IDoctorService`
   directly, so it needed no change.
2. **Provider capability model + runtime capability discovery** — DONE.
   `ProviderDescriptor` (kind + capability flags) already existed and Privacy
   Audit now reads `IsRemote`/`VoiceCapability.Remote` instead of matching
   provider name strings. Runtime capability discovery (context-length probes
   for llama.cpp `/props` and Ollama `/api/show`, cached, feeding
   `LlmModel.ProbedContextLength` as the default when no user override is
   set) is also done.
3. **Context-pack builder extraction** — DONE. `ContextPackBuilder` is the
   single budget-aware packer used by Chat, Agent, and RAG; traces converged
   on one `TraceRecord` schema with per-surface projections.
4. **Unified memory with scopes** — DONE. `IMemoryStore` carries scopes
   (global/workspace/conversation); workspace notes read/write through the
   same store instead of a separate schema. The Session Usage panel (Feature
   Audit: Merge) is folded into Memories: `MemoriesViewModel` now carries a
   conversation filter (per-conversation memory counts, the old panel's
   whole purpose) and a CSV export scoped to the selected conversation.
   `SessionUsageViewModel`/`SessionUsageDetailViewModel` and their views are
   deleted; the sidebar has one memory entry point instead of two.

Also in 1.x: collapse single-implementation interfaces opportunistically —
partially DONE (see docs/review/06-technical-debt.md item 4: provider
routing/label tag-switches collapsed; the settings-schema half is
deliberately deferred); move CommunityToolkit out of Core — DONE; begin
voice convergence (Kokoro-ONNX default, Python providers marked
advanced/best-effort) — the settings-default and UI-labeling half was
already done before this pass (Kokoro is the default, `VoiceProviderCategory`
already marks XTTS/F5 "Advanced"); the "ONNX" half is not — Kokoro is still
a Python 3.12 subprocess, and making it native ONNX Runtime inference is a
feature build correctly scoped at 2.0 ("voice convergence completes"), not
1.x cleanup (see docs/review/02-dependency-review.md); AvaloniaEdit usage
audit — DONE (see docs/review/02-dependency-review.md: one read-only call
site, kept rather than replaced).

**1.x roadmap status: no open items remain.** All four debts above are
DONE, the opportunistic collapses are DONE or deliberately scoped, and the
1.0 hardening checklist above is DONE. The next work on this roadmap is
2.0-horizon by design (below), not accumulated 1.x debt.

## Medium-term — 2.0 (composition becomes the product)

Built *on* the 1.x refactors, in dependency order:

1. **Workspace as the organizing concept — DONE.** An in-repo `.aether/workspace.json`
   manifest (`WorkspaceManifestService`) records preferred model, embedding
   model, linked RAG dataset, and instruction paths; `WorkspaceActivationService`
   reads it (falling back to the pre-existing app-side `WorkspaceProfile` when
   no manifest exists) and the Agent panel applies the activation atomically
   after analysis. A "Save as Workspace Defaults" action writes the manifest so
   it ships with the repo. Chat carries an optional, opt-in activation path for
   the same manifest. Memory scope needed no change: `MemoryScope.Workspace`
   already addresses by normalized workspace root.
2. **Agent: approval-gated command execution — DONE.** `run_command` executes
   only a recipe the workspace itself declared safe (`AllowedCommands` on the
   `.aether/workspace.json` manifest from item 1, sourced from the existing
   command-recipe detection), and only if that recipe also appears in a fixed,
   hardcoded allowlist (`dotnet build`/`dotnet test`/`npm test`/`cargo test`/
   `pytest`) so a hand-edited manifest can never smuggle in an arbitrary
   command. The model selects a recipe by name; it never constructs a command
   line. Always requires approval through the existing review queue (never
   auto-allowed even though the recipe is "safe"), runs workspace-root-bound
   with a 5-minute timeout, and audits through the same trace/tool-result
   trail as every other tool.
3. **MCP client as the tool surface — DONE (stdio transport).** `Aether.Mcp`
   speaks JSON-RPC 2.0 over the stdio of locally spawned MCP servers
   (handshake, `tools/list`, `tools/call`); no remote HTTP/SSE transport, by
   scope decision, since local-first stdio servers cover the common case.
   Servers are configured in Settings and each discovered tool is exposed as
   `mcp:{serverId}:{toolName}` through the existing tool-execution seam via a
   thin `IMcpToolBridge` interface in `Aether.Core`, so `Aether.Agent` never
   references `Aether.Mcp` directly (new architecture test). Every `mcp:` call
   always requires approval, regardless of what the server claims about
   itself, giving extensibility without a proprietary plugin API.
4. **Headless core / local API — DONE.** The non-UI service graph moved into
   `Aether.Composition` (`AetherServiceRegistration.AddAetherCoreServices`),
   shared by `Aether.Desktop` and a new `Aether.LocalApi` host. The host is
   off by default, binds `127.0.0.1` only, and exposes the minimal read/action
   surface decided for this pass: chat completion, memory query, and RAG
   query (no agent/benchmark/settings endpoints). Every request requires a
   token (generated from Settings, resolved through the existing
   `ISecretStore`); the host fails closed with a 503 rather than allowing
   unauthenticated access when no token is configured yet, matching the same
   secret/consent posture as the rest of the app.
5. **Voice convergence, native Kokoro-ONNX — DONE, now the default.** A new
   `Aether.Voice` project hosts `NativeKokoroVoiceProvider`: an English-only
   phonemizer (dictionary plus letter-fallback rules, an explicitly scoped
   stand-in for misaki, not a port of it), a static phoneme-to-id tokenizer
   matching Kokoro's real ONNX vocabulary, and ONNX Runtime inference. Model
   and voice assets follow the same lazy-load/SHA256-pinned-download posture
   as the RAG reranker: never downloaded on the synthesis path, only through
   an explicit Doctor install action (surfaced prominently since it's now the
   default: the Doctor "Voice backend health" check reports a `Warning` on a
   fresh install until the one-time download completes). Registered as
   `VoiceProvider.KokoroNative`, now `TtsSettings.VoiceProvider`'s default and
   `VoiceProviderRegistry`'s fallback; the Python-based Kokoro provider is
   demoted to `Advanced` alongside XTTS v2/F5-TTS as a fallback path, not
   removed. `LocalAiSetupService`'s readiness scan no longer asks for a Python
   venv when the active provider needs none (native Kokoro, OpenAI).

**2.0 roadmap status: no open items remain.** All five items above have
landed, including the default flip for voice convergence.

## Long-term vision (2.x+)

- Aether as the machine's AI substrate: other apps' AI features quietly
  backed by Aether's local API; per-app data-flow visibility in Privacy Audit.
- **Provenance everywhere — phase 1 DONE, in progress.** Goal: any answer can
  be traced to memories, chunks, files, and tool output — "citations"
  generalized from RAG to the whole product. `Aether.Core.Models.SourceReference`
  (a `Kind`/`Title`/`Locator`/`Snippet`/`Score`/`Timestamp` record) is the new
  shared shape. Phase 1 closed a real data-loss bug at the Agent/RAG seam:
  `RetrievedChunk` (the agent's minimal retrieval contract, `Aether.Core.Services`)
  was dropping the source file/path entirely; it now carries a `Locator`, and
  `AgentRetrievedItem`/`AgentContextItemViewModel` propagate it through to the
  "Retrieved Context" panel in the Agent view. `AgentToolResult` gained an
  optional `SourceReference? Source`, populated for tools with one clear
  locator (`read_file`, `summarize_file`, `draft_patch`, `apply_draft_patch`,
  and `mcp:*` calls), left null where there isn't one (`run_command`,
  `list_files`). **Deferred, explicitly not done yet:** RAG's own citation
  pipeline (`RagQueryTrace`/`RagTraceChunk`, the `__RAG_SOURCES__` stream
  protocol) still uses its own shape rather than `SourceReference` — converging
  it is a wire-format change to a working, user-facing feature and warrants
  its own pass. Memory (`Memory.SourceConversationId`) still only records
  which conversation a memory came from, not a structured source reference —
  adding one needs a `MemoryStore` schema migration, not done here. Chat does
  not yet consume RAG or memory citations at all (today it doesn't call RAG,
  and memory injection into the prompt is wired but unused from
  `ChatViewModel`) — a chat "Sources" panel is net-new UI, not part of this
  slice.
- Multi-machine sync of the data root via user-owned transport (file sync,
  no Aether cloud) — sovereignty preserved.
- Agent workflow composition — only if 2.0's execution slice demonstrates
  real sequencing demand (see Opportunities #9).
- Model landscape hedging: keep the runtime layer thin enough that whatever
  replaces GGUF/llama.cpp is an adapter, not a rewrite.

## Explicit non-goals at every horizon

Hosted services, accounts, telemetry, in-process plugin API, provider
failover, vector databases, an ORM, a web UI. Each has a rationale in
documents 02/03/08; write them down in CONTRIBUTING so the "no" survives
contributor enthusiasm.
