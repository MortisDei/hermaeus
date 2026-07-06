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

1. **Workspace as the organizing concept.** Project-level AI configuration
   (`.aether/` in-repo config): opening a workspace activates its model,
   dataset, instructions, and memory scope. This is mostly wiring after 1.x.
2. **Agent: approval-gated command execution.** The next capability slice,
   under the unchanged constitution (risk classes, review queue, traces).
   Safe-command recipes from workspace profiles become the on-ramp.
3. **MCP client as the tool surface.** External tools via an open protocol at
   a process boundary, every call risk-classified — extensibility without a
   proprietary plugin API.
4. **Headless core / local API.** The composition root hosts the same
   services without the UI; a localhost API (with the same secret/consent
   posture) lets editors and scripts use Aether's models, memory, and RAG.
5. Voice convergence completes: Python-venv providers deprecated.

## Long-term vision (2.x+)

- Aether as the machine's AI substrate: other apps' AI features quietly
  backed by Aether's local API; per-app data-flow visibility in Privacy Audit.
- Provenance everywhere: any answer can be traced to memories, chunks, files,
  and model versions — "citations" generalized from RAG to the whole product.
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
