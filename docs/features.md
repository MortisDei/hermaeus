# Features

This is the current capability catalogue for the beta line. It answers what
Hermaeus can do and points to the detailed reference for each substantial
subsystem. It is not a changelog, implementation diary, architecture review,
or roadmap. Release status is recorded in `CHANGELOG.md` and the version is
defined by `Directory.Build.props`.

## Chat

- Native markdown chat with virtualized conversations, code blocks, file and
  image attachments, conversation search, folders, tags, pins, archive, export,
  saved code artifacts, and conversation branching.
- Regenerate and edit create new conversation branches without destroying the
  original. Streaming keeps a reader's scroll position when the transcript is
  no longer pinned to the bottom.
- Context Inspector and a per-answer context receipt show what was prepared and
  injected. Attached Knowledge, Memories, Recall, Project State, attachments,
  token estimates, and provider-reported usage remain distinguishable.
- Reasoning is a separate, labelled transcript channel when the selected route
  provides it. It is preserved and replayed only when the runtime and template
  evidence supports that behavior.
- Normal Chat does not expose web access, a shell, tools, or Agent workspace
  actions. Remote providers receive the prompt and any enabled context sent to
  them.
- Compare Models sends the same draft to up to four selected models without
  adding the comparison to conversation history.

See the [user guide](user-guide.md) for the release-user workflow and [RAG](rag.md)
for Knowledge behavior in Chat.

## Models and Services

- Models manages local GGUF files, provider-discovered models, model profiles,
  sampling defaults, visibility, tags, source provenance, updates, deletion,
  and hardware-fit information.
- Services manages local runtime processes and files. Managed `llama.cpp`,
  Ollama, and OpenAI-compatible profiles are supported, with explicit
  localhost, model, port, and launch configuration.
- Data-root changes use an explicit confirmation and the existing safe
  migration boundary. Ordinary Settings autosave does not move an existing
  workspace, and llama.cpp pruning only deletes validated owned superseded
  version directories while protecting the selected runtime.
- Managed llama.cpp update and recovery preserve the configured or previously
  installed backend class, match platform-specific upstream assets, and refuse
  a missing or unlaunchable GPU backend instead of silently installing CPU.
  Known upstream archive wrapper directories are removed at the owned version
  boundary; legacy nested installations remain discoverable and protected.
- Managed llama.cpp supports GPU-layer placement, K/V cache choices, Flash
  Attention, context shift, CPU-MoE placement, vision projectors, and
  capability-gated speculative decoding. Unsupported or unproven runtime
  features stay `Unknown` or `Unavailable`; Hermaeus does not guess from a
  filename or a generic flag.
- Services keeps a managed server's configured projector path separate from
  its `Use projector` choice. Turning projector use off retains the path and
  linked provenance for repair, while omitting `--mmproj` from the launch;
  turning it back on restores the verified configured path.
- Model downloads retain source and hash identity. Shards, verified vision
  projectors, and MTP companions are handled as a model file set when their
  relationship is proven. Ambiguous companions require review, and removing
  known companions is an explicit Keep, Remove, or Cancel choice.
- GPU Fit is a deterministic prediction over the current editor values. It
  names weights, K/V cache, runtime overhead, companions, placement, and
  headroom while keeping missing inputs as `Unknown`. Runtime observations are
  separate and comparable only under a compatible fingerprint.
- Settings preferences save automatically. Process, model, and runtime changes
  retain explicit save/apply actions because they can affect files or running
  services.

Read [Models and Services in the user guide](user-guide.md#models-and-services)
and the [llama.cpp reference](llama-cpp-features.md) for operational details.

## RAG and Knowledge

- Structure-aware ingest supports Markdown, code, text, and digital PDFs, with
  heading, symbol, page, and log-aware chunk metadata.
- Retrieval combines planned query variants, keyword search, embeddings,
  optional ONNX reranking, structural boosts, confidence-aware refusal, and
  budget-aware context packing.
- Watched sources use cancellable drift scans. Refresh applies only new and
  changed files by default; removing missing sources remains a separate,
  explicitly confirmed action. Automatic refresh is off by default.
- Dataset Manager exposes source and chunk health, embedding identity,
  dimensions, missing and stale files, duplicate rows, index size, and reindex
  state. A dataset embedded with another model falls back to keyword retrieval
  or requires reindexing according to the operation.
- Chat Knowledge injection is bounded and cited. Weak retrieval adds nothing
  rather than forcing unrelated chunks into a response.
- RAG has a native evaluation harness with retrieval metrics, refusal handling,
  cancellation, and export. The separate [eval harness plan](rag-eval-harness.md)
  describes proposed expansion beyond the shipped surface.

See the [RAG reference](rag.md).

## Memories

- Memories are a local, reviewable store of durable facts with categories,
  tags, scopes, importance, recall statistics, pinning, archive, expiry, and
  confirmed deletion.
- Search blends full-text and embedding similarity when an embedding model is
  available, with a bounded keyword fallback when it is not. Archived and
  expired memories are excluded from search and injection.
- Chat can propose, update, or forget memories through bounded markers. Only a
  memory actually injected into that turn can be updated or forgotten, and
  marker syntax never reaches the persisted transcript.
- Auto-summary extracts structured memory metadata in the background. Providers
  that can enforce the required response shape use that constraint, while the
  existing parse-and-repair fallback remains for other providers.
- Agent lessons are a separate, reviewable store. Optional read-only use of
  Global-scope lessons in Chat never changes the Agent safety gate.

## Recall

Recall is one local search index over conversations, Agent tasks, Memories, and
RAG chunks. Its command-palette search is distinct from RAG retrieval and
Memory injection. Recall indexing is visible and clearable, and optional Chat
injection is off by default. Chat traces label keyword-only Recall as degraded
retrieval even when lexical hits are usable. See the [Recall reference](recall.md).

## Agent Workbench

- Agent is a supervised local task runner with explicit goals, persisted task
  state, step transcripts, context receipts, workspace profiles, and a review
  queue.
- Read-only inspection can run within the selected workspace. Writes,
  commands, MCP calls, and sub-task planning remain approval-gated and are
  classified deterministically by the safety gate.
- The workbench exposes the current decision, live progress, plan, response,
  changes, approvals, reservations, commands, and unfinished work. A run ledger
  supports per-file Rewind with staleness checks.
- Workspace policy narrows paths and read budgets. Lessons inform the model
  but never widen authority. Native tool calling and constrained JSON planner
  output are alternate transport paths through the same gate.
- Sub-task orchestration is bounded to one child level. Each child may inherit
  the parent model or use an explicitly selected visible model; identities and
  outcomes remain visible through synthesis.
- The Local API exposes an Agent contract and policy, but Agent execution routes
  are not mapped until Desktop and Local API share one serialized task owner.

See [Agent Workbench](agent.md) and the [Agent Local API contract](agent-api.md).

## Projects

Projects provide shared defaults for Chat, Agent, and RAG: folder root, model,
dataset, system prompt, and a fixed brand color. Optional Project State is
revisioned, user-owned, reviewable, and injected only after acceptance. It is
separate from Memories, Recall, RAG, conversations, and Agent task state.

See the [Projects reference](projects.md).

## Lab and Evidence

Lab runs controlled measurements in an isolated loopback runtime without
stopping Chat, changing saved settings, or selecting a winner automatically.
Definitions are frozen, candidates are bounded, valid recipe evidence flows
directly into candidate review, missing measurements remain missing, and output
correctness gates comparisons and Apply review.

The Evidence surface stores typed Agent, GPU Fit, and Lab records with source
links, fingerprints, corrections, redacted export, and confirmed removal.
Experience is descriptive evidence only. It never grants approval, changes a
safety decision, or rewrites the analytical GPU Fit prediction.

See the [Lab reference](lab.md).

## Benchmarks

Benchmarks run reusable local prompt suites, retain immutable run history, record
failures and runtime metadata, compare models over shared cases, and export
Markdown, JSON, and CSV. Ranking profiles, Best overall, Best across every
suite, and the fixed Speed Check answer different questions and do not collapse
missing evidence into a score.

Benchmark resource readings distinguish process RAM, device totals, and honest
per-process `Unknown` values. Lab owns controlled configuration experiments;
Benchmarks own reusable suites and run comparisons.

See the [Benchmarks reference](benchmarks.md).

## Voice

Voice is optional and off by default. Native Kokoro, managed local providers,
and remote OpenAI voice are supported, with provider-specific setup and
configuration. Local speech recognition uses an in-process Whisper model when
installed; remote transcription is explicit. Captured and uploaded audio is
transient and is not persisted or attached to conversations.

Audio feedback is a separate semantic cue service with explicit events,
volume, mute, visual equivalents, bounded queueing, and suppression while TTS
speaks. It does not cue ordinary clicks, token arrival, navigation, or high GPU
use.

See the [Voice reference](voice.md).

## Doctor and Setup

The setup wizard makes Data Root, AI Assets, runtime, model, and optional voice
choices explicit. Doctor checks actual paths, executable readiness, runtimes,
models, storage, RAG, voice, GPU, secrets, and update information. Remediation
actions show their target and plan before a user approves them.

Doctor, Services, and Activity report observed state. They do not turn missing,
unreachable, or unverified components into a false Ready state.

See [First launch and troubleshooting](user-guide.md) and [Packaging](packaging.md).

## System, Logs, Activity, and Settings

- System shows app, operating-system, CPU, RAM, storage, database, managed
  component, and best-effort GPU information.
- Activity records observed outcomes for operations such as model downloads,
  server lifecycle, ingest, backups, restores, memory sweeps, and voice.
  Artifact-specific rows open their artifact; rows without one stay inert.
- Runtime Logs apply redaction before display and persistence. Settings holds
  user preferences, while Services holds process and file configuration.
- The local API is an off-by-default, loopback-only surface for selected Chat,
  embedding, Memory, RAG, model, and capability operations. It uses named
  tokens and does not expose Agent execution in this beta.
- Privacy Audit shows which configured features can send content to remote
  providers and the status of local protections.

See the [Local API reference](local-api.md), [Security Review](security-review.md),
[Testing](testing.md), and the [user guide](user-guide.md).

## Security and privacy posture

Hermaeus keeps models, data, and workflows local where possible. Managed
services bind to localhost, process launches use argument lists rather than
shell strings, secrets are stored through secret-store references, downloads
are hash-verified when trusted hashes exist, and user-state writes use atomic
replacement. Backup, restore, path, symlink, redaction, and approval controls
are documented in the [current security review](security-review.md).

Open hardening items live in the [security roadmap](security-roadmap.md). The
[security history](security-history.md) is historical and does not override the
current review.
