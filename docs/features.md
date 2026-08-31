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
  and hardware-fit information. Its bounded Services-owned inventory rechecks
  file identity and reuses GGUF metadata until an explicit invalidation or a
  file change. A saved auto-tune profile is shown directly on
  the model card with its GPU layers, threads, and context. Opening the model
  configuration also hydrates the editable saved tune values from that shared
  profile; saving them remains separate from the runtime Save Config action on
  Services.
- The Models catalog is organized into **Chat & Generation**, **Embeddings**,
  and **Rerankers**. Role sections come from provider configuration, dedicated
  asset layout, GGUF metadata, and trusted manifest provenance. Factual badges
  such as MoE, MTP, Draft, and Vision / Projector remain separate from
  configured or ready state. Proven companions stay owned by their primary
  model card with Present, Missing, Stale, or Unknown detail; they do not
  become standalone cards merely because they are GGUF-like files. Search
  covers roles, capabilities, tags, and companion state.
- Services manages local runtime processes and files. Managed `llama.cpp`,
  Ollama, and OpenAI-compatible profiles are supported, with explicit
  localhost, model, port, and launch configuration.
- Data-root changes use an explicit confirmation and the existing safe
  migration boundary. Ordinary Settings autosave does not move an existing
  workspace, and llama.cpp pruning only deletes validated owned superseded
  version directories while protecting the selected runtime.
- Managed llama.cpp update and recovery honor explicit backend choices. Auto is
  re-evaluated from current hardware when installation is required, may use a
  compatible accelerated fallback, and records the selected backend separately
  from the still-Auto preference. Missing or unlaunchable GPU backends are
  refused instead of silently becoming CPU.
  Known upstream archive wrapper directories are removed at the owned version
  boundary, while flat upstream packages are accepted as well; mixed layouts
  fail closed and legacy nested installations remain discoverable and protected.
- Managed llama.cpp supports GPU-layer placement, K/V cache choices, Flash
  Attention, context shift, CPU-MoE placement, vision projectors, and
  capability-gated speculative decoding. Unsupported or unproven runtime
  features stay `Unknown` or `Unavailable`; Hermaeus does not guess from a
  filename or a generic flag. Doctor recognizes GPU backend shared libraries
  on Windows and Linux, and reports installed runtime identity separately from
  an unavailable latest-release comparison.
- Nested scroll surfaces give the wheel to the content under the pointer and
  bubble to a page only when that content reaches its edge. Horizontal wheel
  input is retained where a pane provides horizontal overflow.
- Services keeps a managed server's configured projector path separate from
  its `Use projector` choice. Turning projector use off retains the path and
  linked provenance for repair, while omitting `--mmproj` from the launch;
  turning it back on restores the verified configured path.
- Model downloads retain source and hash identity. Shards, verified vision
  projectors, and MTP companions are handled as a model file set when their
  relationship is proven. Ambiguous companions require review, and removing
  known companions is an explicit Keep, Remove, or Cancel choice.
- Hugging Face repository artwork is optional decoration only. It is read from
  the selected card's bounded `cardData.thumbnail`, pinned to the exact
  repository revision and tree when it names a repository file, fetched only
  through the reviewed Hugging Face host and delivery-host policy, and
  independently checked for MIME, magic, format, animation, dimensions, and
  size before Avalonia decode. The artwork cache is bounded, LRU-evictable,
  excluded from Data Root backups, and has a separate Clear action. A model
  update check may backfill artwork when its verified manifest revision matches
  the fetched card and tree; decoration failures never fail the update check.
- GPU Fit is a deterministic prediction over the current editor values. It
  names weights, K/V cache, runtime overhead, companions, placement, and
  headroom while keeping missing inputs as `Unknown`. Runtime observations are
  separate and comparable only under a compatible fingerprint.
- System Overview shows the current whole-workload resource snapshot: registered
  Chat, embeddings, Lab, voice, and in-process consumers; predicted or observed
  device and system allocations; whole-device totals; and explicit Unknown
  reasons. Services shows the admission receipt for each managed start.
  Admission uses short-lived reservations to prevent concurrent approvals from
  relying on the same stale headroom. It never stops, unloads, or changes
  another consumer, and Unknown is never treated as zero.
- Managed servers have an opt-in adaptive launch envelope. Fixed is the
  default, Advise plans without launching, and AdaptAtLaunch may try only a
  bounded single-axis GPU-layer, context, KV, or known MoE compromise that the
  saved envelope allows. Each attempt uses fresh whole-workload admission and
  an auditable structured runtime observation; Unknown effective placement or
  context stops adaptive launch, and no transient candidate is written back to
  saved settings. A recent compatible successful launch may be preferred only
  when its exact runtime, model, hardware, base configuration, workload, and
  bounded evidence age match; it never skips fresh admission. Unsupported fit,
  cache, and multi-device behavior remains Unavailable or Unknown.
- Model cards use the detailed versioned prediction when local GGUF shape
  metadata is available. Remote or pre-download cards retain a clearly labelled
  rough pre-download estimate until that metadata exists.
- Settings preferences save automatically, including a pending edit flushed by
  clean shutdown. Process, model, and runtime changes retain explicit
  save/apply actions because they can affect files or running services.

Read [Models and Services in the user guide](user-guide.md#models-and-services)
and the [llama.cpp reference](llama-cpp-features.md) for operational details.

## RAG and Knowledge

- Structure-aware ingest supports Markdown, code, text, and digital PDFs, with
  heading, symbol, page, and log-aware chunk metadata.
- Retrieval combines planned query variants, keyword search, embeddings,
  optional ONNX reranking, structural boosts, confidence-aware refusal, and
  budget-aware context packing.
- The pinned ONNX reranker is loaded only from verified assets. Asset changes
  invalidate the old session cleanly, while normal ranking remains sequential;
  a separate bounded batch diagnostic does not silently change query behavior.
- Watched sources use cancellable drift scans. Refresh applies only new and
  changed files by default; removing missing sources remains a separate,
  explicitly confirmed action. Automatic refresh is off by default.
- Dataset Manager exposes source and chunk health, embedding identity,
  dimensions, missing and stale files, duplicate rows, index size, reindex
  state, and published generation history. A dataset embedded with another
  model falls back to keyword retrieval or requires reindexing according to the
  operation. RAG publication is atomic, and source citations retain the exact
  generation, source revision, and content hash that supplied the chunk.
- Chat Knowledge injection is bounded and cited. Weak retrieval adds nothing
  rather than forcing unrelated chunks into a response.
- RAG has a native evaluation harness with retrieval metrics, refusal handling,
  cancellation, and export. The separate [eval harness plan](rag-eval-harness.md)
  describes proposed expansion beyond the shipped surface.

See the [RAG reference](rag.md).

## Memories

- Memories are a local, reviewable store of durable facts with categories,
  tags, scopes, importance, recall statistics, pinning, archive, expiry, and
  confirmed deletion. Pinned rows show a persistent Pinned state and an Unpin
  action instead of relying on a transient notification.
- Search blends full-text and embedding similarity when an embedding model is
  available, with a bounded keyword fallback when it is not. Archived and
  expired memories are excluded from search and injection.
- Chat can propose, update, or forget memories through bounded markers. Only a
  memory actually injected into that turn can be updated or forgotten, and
  marker syntax never reaches the persisted transcript.
- Auto-summary extracts structured memory metadata in the background. Providers
  that can enforce the required response shape use that constraint, while the
  existing parse-and-repair fallback remains for other providers.
- Each memory keeps a stable assertion id and immutable content revisions with
  recorded time, optional established effective time, source references,
  decisions, and explicit Current, Superseded, Disputed, or Archived state.
  Current Chat receipts identify the exact revision; superseded and disputed
  revisions are excluded from ordinary injection.
- Memories offers a linear revision timeline with adjacent content diffs,
  source and decision details, explicit revise/correct/dispute/restore actions,
  and review-only contradiction proposals. A versioned redacted JSON export
  preserves assertion, revision, effective-time, source, and decision
  structure. The existing CSV export remains current-projection-only.
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

Lab runs controlled measurements in an isolated loopback runtime. Only one
manual or guided run may be active at a time. A running
selected Chat source is fully stopped and awaited before the isolated process
starts, then restored only if its complete configuration is unchanged. Lab does
not change saved settings or select a winner automatically. Definitions are
frozen, candidates are bounded, valid recipe evidence flows directly into
candidate review, missing measurements remain missing, and output correctness
gates comparisons and Apply review. Failures retain their useful operation
detail. One Lab execution is one top-level Evidence entry keyed by its durable
run id. Its drill-down retains the completion result, configuration slices,
provenance, and raw detail. Completion summaries name the eligible candidate or
state why no recommendation is available, while Apply remains an explicit
review and confirmation owned by the Hermaeus window. The result card leads
with the experiment, recorded model identity when available, status,
timestamps, tested configurations, recommendation state, correctness, and
measured or predicted resource deltas. Missing measurements remain `Unknown`.

The Evidence surface stores typed Agent, GPU Fit, Lab, and adaptive-launch records with source
links, fingerprints, corrections, redacted export, and confirmed removal.
Experience is descriptive evidence only. It never grants approval, changes a
safety decision, or rewrites the analytical GPU Fit prediction.

Successful auditable adaptive changes and correctness-gated Lab winners can
produce a shared review card. The card shows current and proposed fields,
evidence, trade-offs, target identity, and freshness. Apply and Undo are
stale-guarded settings transactions; they never restart a running server.
Benchmark model guidance is review-only and can be dismissed or opened in
Models, but it never changes the selected model automatically.

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
configuration. Per-channel pickers use the active provider's discovered voice
names without hardcoding a provider catalogue. Local speech recognition uses
an in-process Whisper model when installed; remote transcription is explicit.
Captured and uploaded audio is transient and is not persisted or attached to
conversations.

Native Kokoro health failures retain the provider's observed diagnosis in
Services and link directly to Doctor. Doctor owns the verified asset repair
action; a healthy native provider is not offered as an install action.

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

- A second normal launch exits immediately when the existing per-user file
  lock is held. It does not contact, activate, or change the existing
  instance. The lock remains the cross-process data-safety gate; package
  install and uninstall helpers remain separate utility launches.
- Conversation deletion from its details flyout keeps confirmation beside the
  initiating control. The context-menu path retains a full confirmation dialog
  because it has no anchored details surface.
- System shows app, operating-system, CPU, RAM, storage, database, managed
  component, and best-effort GPU information.
- Chat telemetry can sample the currently active managed server process. Its
  process RAM and per-process GPU readings are tied to that process identity;
  missing counters remain Unknown.
- Activity records observed outcomes for operations such as model downloads,
  server lifecycle, ingest, backups, restores, memory sweeps, and voice.
  Artifact-specific rows open their artifact; rows without one stay inert.
- Runtime Logs apply redaction before display and persistence, omit repetitive
  low-level llama slot scheduler chatter from the normal persistent sink, and
  rotate with bounded file-count, age, and total-size retention. Settings holds
  user preferences, while Services holds process and file configuration.
- The local API is an off-by-default, loopback-only surface for selected Chat,
  embedding, Memory, RAG, model, and capability operations. It uses named
  tokens, applies token and port changes by restarting its owned child, and
  does not expose Agent execution in this beta.
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
