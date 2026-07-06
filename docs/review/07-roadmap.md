# Roadmap (architecture-first)

Principle: 1.0 ships what exists, hardened. 1.x pays structural debt while the
surface is still small. 2.0 is composition, not features. Features that appear
below exist only because an architectural change makes them nearly free.

## Immediate — 1.0 (ship what exists, harden it)

Goal: a release you can stand behind for years. No new subsystems.

- **Freeze feature growth.** Everything in Feature Audit rated Essential
  ships; Merge/Deprecate items are frozen, not fixed.
- **Break the `ILlmService` contract now** (last cheap chance): options record
  for sampling params, single streaming API, pull/delete moved off the base
  interface.
- Retention policies for traces, logs, benchmark history, tune profiles.
- Unit-test the security-load-bearing code: risk classifier, workspace path
  boundaries, redaction patterns, backup extraction guards, migration runner.
  Adopt standard `dotnet test` in the process.
- Architecture tests: ViewModels never reference Avalonia; ONNX Runtime never
  escapes `Aether.Rag`; `Aether.Core` gains no new packages.
- Docs-truth pass (versions, "should" statements out of features.md); license
  review as already gated.
- Pin Avalonia; document the upgrade playbook.

## Near-term — 1.x (pay the four debts)

Each is independently shippable and each *deletes* code:

1. **Check/fix registry** — Doctor, Setup scan, Trust & Safety, Privacy Audit
   become filtered views over one contribution-based inspection engine.
   Doctor Fixes queue (already designed in docs) falls out of this.
2. **Provider capability model + runtime capability discovery** — registry
   with descriptors and probed capabilities; Composite becomes a router;
   Privacy Audit and Services UI read capabilities instead of provider names.
3. **Context-pack builder extraction** — one budget-aware packer with
   provenance, used by Chat, Agent, and RAG; Context Inspector and all traces
   read from it. Unify the three trace shapes onto one schema while there.
4. **Unified memory with scopes** — one service, one panel, one retrieval
   path; migrate chat memory, workspace memory, and profile facts. Session
   Usage panel retires.

Also in 1.x: collapse single-implementation interfaces opportunistically;
move CommunityToolkit out of Core; begin voice convergence (Kokoro-ONNX
default, Python providers marked advanced/best-effort); AvaloniaEdit usage
audit.

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
