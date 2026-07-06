# Architectural Opportunities (2026-07)

Each candidate is evaluated, not assumed. "Adopt", "Defer", or "Reject" — with
the problem it solves. The bar: every abstraction must solve a problem Aether
demonstrably has today or will unavoidably have.

## 1. Provider capability model — ADOPT (highest priority) — DONE

**Problem today:** `CompositeLlmService` hard-codes three providers with
if-chains and settings flags; `PullModelAsync`/`DeleteModelAsync` sit on
`ILlmService` even though only Ollama implements them; Doctor, Services UI,
and Privacy Audit each re-derive "what can this provider do" independently.

**Shape:** a small `ProviderDescriptor` per registered provider:
identity, kind (managed-local / local-api / remote-api), and capability flags
(streaming, usage-reporting, model-pull, model-delete, embeddings, tool-calls,
vision, max-context discovery). Composite becomes a registry that routes by
model→provider mapping and exposes capabilities to the UI ("this model can't
report usage, estimates shown"). Privacy Audit reads `kind == remote-api`
instead of maintaining its own provider list. Strip pull/delete from
`ILlmService` into an optional `IModelLibrary` capability.

This is not speculative flexibility — it deletes existing duplication in four
places and is the precondition for tool-calling agents (next item).

`ILlmService` never carried `PullModelAsync`/`DeleteModelAsync` in the
reviewed codebase state, so that specific leak was already a non-issue;
`ProviderDescriptor` (kind + capability flags) exists and Privacy Audit reads
it instead of matching provider name strings.

## 2. Runtime capability discovery — ADOPT, minimal form — DONE

**Problem:** context-window size, tool-call support, and vision support are
currently user-asserted or guessed. llama-server `/v1/models` and Ollama
`/api/show` expose real metadata. Populate the capability model above from
live probes, cached per model. Do not build a general "capability negotiation
protocol" — a probe-and-cache function per provider is enough.

**Shipped as the minimal form:** `LlamaCppService` probes `/props`
(`default_generation_settings.n_ctx`) once per base URL; `OllamaService`
probes `/api/show` per model, reading `model_info["{architecture}.context_length"]`.
Both cache successful results in-memory and populate
`LlmModel.ProbedContextLength`; `ModelProfileService.ApplyProfiles` falls back
to it when no explicit per-model `DefaultContextSize` override exists, so
`ChatViewModel`'s context-budget math (which already reads
`SelectedModel.DefaultContextSize`) uses a real number instead of a guess.
Tool-call/vision probing was not built — no consumer reads those flags yet
(see #3, and the "no speculative flags" principle this doc already states).

## 3. Model capability registry — MERGE INTO #1/#2, do not build separately

A standalone registry (curated metadata about model families) is maintenance
quicksand: the model landscape churns monthly and you'd be signing up to
curate it forever. Derive capabilities from probes + GGUF metadata + benchmark
history you already store. Reject as an independent subsystem.

## 4. Unified memory with scopes — ADOPT (see Architecture Review §2) — DONE

One `IMemoryService` with scope = global / workspace / conversation, one
retrieval path, one UI. Chat memory, workspace memory, and workspace-profile
facts become rows with different scopes, not different subsystems. This is the
change that makes features "compose naturally rather than becoming isolated
modules" — currently the memory features are the clearest violation of that
principle.

## 5. Check/fix registry (Doctor, Setup, Trust, Privacy Audit) — ADOPT — DONE

Four systems today independently inspect the installation. Define one
contract: a check has an id, subsystem, severity, message, and optionally an
approval-gated fix action (the docs already describe the desired "Doctor Fixes
queue"). Doctor runs all checks; Setup Wizard runs the first-run subset; Trust
& Safety runs the security subset; Privacy Audit runs the data-flow subset.
Each subsystem contributes its own checks, so adding a feature never means
editing DoctorService again. This is a simplification disguised as an
abstraction — it should reduce net code.

## 6. Context-pack builder as a first-class service — ADOPT — DONE

Chat builds context (system prompt, memory injection, attachments, history
budget) inside `ChatViewModel`; the Agent builds context packs inside
`Aether.Agent`; RAG packs context inside `RagPipeline`. Three budget-aware
packers. Extract one `ContextPackBuilder` (sections, budgets, provenance) used
by all three. Payoff: the Context Inspector, Chat Trace, agent traces, and RAG
traces all get the same provenance data for free, and token-budget bugs get
fixed once. This is the heart of the product; it deserves to be a named,
tested component rather than ViewModel private methods.

## 7. Plugin architecture — REJECT for 1.x, revisit narrowly at 2.0

Nothing currently needs third-party extension, there is no ecosystem to serve,
and a plugin boundary is the most expensive abstraction you can ship (ABI
stability, sandboxing, trust model — especially painful given the security
posture). The philosophy says features should compose internally; do that
first. The only plugin-shaped thing worth considering at 2.0 is **MCP client
support** (see #8), which gives extensibility via a protocol you don't have to
own, at a process boundary, with the existing approval-gate model — strictly
better than an in-process plugin API.

## 8. Tool orchestration via MCP client — ADOPT at 2.0 horizon

When the Agent graduates from read-first to approval-gated execution, do not
invent a proprietary tool interface. Implement Model Context Protocol as a
*client*: local MCP servers become the tool surface, every call flows through
the existing risk classifier and review queue, and Aether inherits a large
open tool ecosystem with zero vendor lock-in (it's an open protocol, multiple
providers). This is the rare case where adopting an external standard reduces
long-term surface instead of growing it.

## 9. Workflow composition / task orchestration — DEFER

Multi-step agent workflows, DAGs, pipelines: seductive, premature. The agent
does one goal at a time with explicit state — that is a feature, not a
limitation, at this maturity. Revisit only after approval-gated command
execution has been in users' hands and real sequencing pain is observed.
Building an orchestrator before the primitives are proven is how projects
acquire their worst code.

## 10. Project-level AI configuration — ADOPT, cheap and high-value

Workspace profiles already record preferred chat model + linked RAG dataset.
Finish the thought: selecting a workspace should activate its full
configuration (model, dataset, system-prompt additions from project
instructions, memory scope). Add a discoverable on-disk form (e.g.
`.aether/config.json` in the workspace) so configuration travels with the
repo. This composes existing features rather than adding one; it is the "glue"
the docs already gesture at.

## 11. Provider failover — REJECT

Failover between LLM providers is a server-fleet concept. On a desktop app,
silently answering with a different model than the user chose is a bug, not
resilience — and for a privacy-focused app, failing over from a local model to
a remote API would be a trust violation. Fail loudly, offer a manual switch.

## 12. Headless core / local API surface — ADOPT direction at 2.0

Once orchestration moves out of ViewModels (Architecture Review §5), the
composition root can host a headless mode: a localhost API/CLI over the same
services. This is what lets Aether be the *substrate* other tools use (see
Vision) rather than another chat window. Do not build it before the ViewModel
extraction — it falls out nearly free afterwards, and is painful before.
