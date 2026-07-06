# System Map — spine, capabilities, tools

This document is the organizing model for the 1.x refactors. It reframes the
feature audit: Aether's problem is not too many features, it is **conceptual
collisions** — the same idea implemented in multiple layers, each believing it
owns the concept. The fix is to name the real systems and turn today's
features into *projections* of them.

Rule: every piece of Aether must land in exactly one bucket below. If it
doesn't land cleanly, it is duplicated or drifting, and that is a design bug
to fix, not a taxonomy problem to argue.

## Core spine (defines Aether — cannot be removed without changing what it is)

- **Agent workbench** — task state, risk gates, patch queue, review flow.
- **Context System** — everything Aether knows and everything it shows the
  model, with provenance. See below.
- **Runtime abstraction** — provider registry, capability model, managed
  llama-server, model routing (`ILlmService` and what sits behind it).
- **Safety/trust model** — risk classification, redaction, secret store,
  path boundaries, the check/fix inspection engine.

## Capabilities (plug into the spine; removable without identity loss)

- RAG (a knowledge source feeding the Context System)
- Model providers (llama.cpp / Ollama / OpenAI-compatible adapters)
- Voice (a readback capability; must not grow its own infrastructure)
- Evaluation System (see below — currently three overlapping features)

## Tools (should feel disposable; must never grow storage or subsystems)

- Tasks/reminders (flagged for deprecation)
- Agent-panel file browser
- UI utilities (toasts, tray, hotkeys, exports)

## The two unifications

### Context System — one system, multiple projections

Today four features each believe they own "what Aether knows / what the model
saw": chat memory, workspace memory (+ profiles), traces (three formats), and
RAG retrieval history. They are one system:

| Today | Becomes |
| --- | --- |
| Chat memories | Context System, **global/conversation scope**, source = extraction |
| Workspace memory + profile facts | Context System, **workspace scope**, source = agent/analysis |
| Chat trace, RAG trace, agent trace | one trace schema, three **projections** (viewers) |
| Context Inspector, agent context pack, RAG packing | one **ContextPackBuilder** with budgets + provenance; three consumers |

Implementation order (each shippable alone) — **all three done**:
1. Unified trace schema (the cheapest collision to fix, and it makes the next
   two observable).
2. ContextPackBuilder extracted from ChatViewModel; Agent and RAG packers
   converge onto it.
3. Memory scopes: one store/service, one panel, scoped rows; old stores
   migrated.

### Evaluation System — one system, multiple projections

Benchmarks, Compare Models, and the RAG eval harness are one question asked
three ways: *"how good is this model/pipeline at this job, on my machine?"*
They should share: a case/run data model, an execution engine, storage of
results, and export. Projections: quick A/B (today's Compare Models),
suite runs (today's benchmarks), retrieval metrics (today's eval harness).
This is a 1.x design note and a 2.0 implementation; the immediate rule is a
freeze: none of the three may grow features independently.

## Dual-ownership register (the landmines)

Tracked explicitly so no new work deepens them:

1. Memory: chat layer vs. agent layer both own persistence of "facts" —
   **RESOLVED**. One scoped `IMemoryStore`; workspace notes are a scope, not
   a separate schema.
2. Traces: three schemas for one concept ("what happened in a send/run") —
   **RESOLVED**. One `TraceRecord` schema, three projections.
3. Context: chat and workspace both own "what the model sees" — **RESOLVED**.
   One `ContextPackBuilder` used by Chat, Agent, and RAG.
4. Evaluation: benchmarks vs. compare vs. evals — **OPEN**. Design exists
   (doc 10); not yet implemented.
5. Inspection: Doctor vs. Setup scan vs. Trust vs. Privacy Audit each own
   "is this install healthy/safe" — **RESOLVED** by the check/fix registry;
   each is a filtered projection of one `IInspectionEngine`.

Only the Evaluation System merge remains open from this register.

New-feature test (add to review checklist): *which existing system does this
project onto?* If the answer is "it needs its own store/panel/checks", the
design is wrong or the system map needs a deliberate amendment.
