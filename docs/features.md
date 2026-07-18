# Features

## Chat

- Chat history with rename, delete, fast FTS-backed search, folders, tags,
  pins, archive, and direct file context injection for selected text/code files.
- Conversation management via right-click context menu with delete, archive,
  export, and pin options for convenient quick access.
- Chat context usage indicator with provider-reported usage where available and
  local estimates before send.
- Context Inspector panel shows the exact context pack before send when opened:
  system prompt, draft message, ready attachments, chat history token estimate,
  and the raw prompt sections that will be sent to the model.
- Chat Trace Viewer records completed sends with selected model/runtime,
  system prompt, attachment count, estimated tokens, provider usage when
  reported, first-token latency, total latency, error details, and a
  pre-stream timing breakdown (memory recall, injection selection, lesson
  context, prompt build, first token) so a slow send is diagnosable without a
  profiler. When llama.cpp reports its own prompt-processing timings, the
  breakdown also shows the server-side prompt tokens/time so a slow send can
  be narrowed to request queuing versus actual prompt evaluation. The
  breakdown also separates the first streamed event of any kind from the first
  visible content token, so a long non-content stream prefix is no longer
  hidden inside "before first token". A send whose pre-first-token wait
  exceeds 10 seconds logs a runtime warning with the full breakdown, and on a
  machine with a real GPU that is still configured for CPU inference the
  warning ends with a "prompt was read at CPU speed" hint pointing at Doctor.
- Compare Models sends the current draft prompt to one to four selected models
  and compares answers, latency, token usage, and simple quality notes without
  adding the comparison run to chat history.
- Selecting a model applies that model's own saved temperature/top-P/top-K/
  max-tokens/penalty defaults (when it has any) without changing the global
  LLM defaults other panels (Benchmark, Agent, RAG) see; a background model
  list refresh no longer resets sampling values you tuned mid-session for
  the model you already have selected, and switching to a different model
  resets any value it doesn't specify back to your global default instead
  of carrying over the previous model's tuning.
- The chat header's temperature control is a compact "T <value>" button that
  opens a sampling flyout with all eight parameters (temperature, top-P,
  top-K, min-P, repeat/frequency/presence penalty, max tokens), matching the
  same ranges and descriptions as the Models page editor, plus a "Reset to
  model defaults" button. These values remain conversation-local and are
  never written back to settings.
- Chat can attach selected local text/code files directly to the next message.
  Use the attach button or drop files on the input. Aether reads each file once at
  send time, prepends a bounded context block to the model prompt, and stores only
  an attachment summary in conversation history.
- Attachment file paths are also persisted with each user message so regenerate
  can reattach context files after an app restart when those files still exist.
- The chat bar shows current context usage against the selected context window.
  It uses provider-reported token usage when available and falls back to a
  local estimate for draft input, visible history, system prompt, and ready file
  attachments. At high usage, Aether warns so a fresh conversation can avoid
  quality loss.

## Memory

- Persistent chat memories with categories (`facts`, `preferences`,
  `learned_behaviors`, `interests`) stored in a local SQLite database.
- Hybrid recall: memory search blends full-text search with cosine similarity
  against an embedding model when one is configured, so a paraphrase with no
  lexical overlap can still surface; falls back to pure FTS/LIKE ranking when
  no embedding model is available, and also if the query embedding itself
  does not complete within 3 seconds. Search never embeds anything but the
  query; rows without their own embedding yet are backfilled by a background
  pass shortly after startup and after memory writes, not on the send path.
- Relevance-aware injection: memories selected for chat context are ranked by
  the search's own relevance score blended with importance, not recency-first
  as before; recency is now only the final tiebreaker.
- Lifecycle: each memory tracks how many times and when it was actually
  recalled (injected), not just retrieved. Effective importance decays for
  memories that go unused; ones that decay below a floor and stay unrecalled
  long enough are auto-archived (never hard-deleted) the next time the
  Memories panel opens. Pinned memories never decay.
- The model can correct or retire a memory it was shown this turn with
  `[MEMORY_UPDATE: <id> | <content>]` / `[MEMORY_FORGET: <id>]` markers; only
  ids actually injected into that turn are honored, everything else is
  ignored and logged.
- Auto-summary now asks for structured JSON (content, category, importance,
  tags) instead of parsing `[MEMORY: ...]` markers with keyword heuristics,
  giving model-supplied metadata directly; the marker format remains the
  fallback if a model doesn't follow the JSON instruction.
- Dedicated Memories panel to review, search, pin, archive, and delete
  memories, sorted by effective importance and showing recall stats.
- Session Usage panel: view per-conversation memory counts and recent activity
  to help triage which conversations have stored memories.
- Configurable memory controls in Settings: global enable, context injection,
  auto-summary threshold, token budget, encryption toggle, and auto-archive
  days.
- Optional chat-side consumption of the Agent's Global-scope self-learning
  lessons (Settings > Memory; off by default): when enabled, chat's system
  prompt gets a read-only "Learned Behaviors (Agent Lessons)" block alongside
  stored memories. Unlike memories, lessons are never editable from chat -
  they carry no id tag, so a model's `[MEMORY_UPDATE]`/`[MEMORY_FORGET]`
  cannot target one; the Agent workbench's Lessons panel remains the only
  place to edit, pin, retire, or delete them.
- Post-response auto-summary pipeline: important conversations are analysed in
  the background and durable memories are extracted and merged with
  deduplication safeguards.
- Chat header now shows a live memory status line so you can see whether memory
  is enabled and how many recent memories are available.

## Agent Workbench

- Local task workbench with explicit task state, a persisted step transcript,
  compact context packs, local logs/traces, and review queue controls.
- Autonomous runs: Start (or resuming after an approval or a reply) runs
  steps back to back without a click per step, stopping at a final answer, a
  question for the user, a gated action needing approval, a blocked or
  failed task, or a configurable step cap (`Agent.MaxAutoSteps`, default
  20) - which now hands the task back for review with a note instead of
  leaving it looking silently active. Live progress and Stop remain
  available; a manual single-step advance is still available too.
- Reply channel: a task waiting on `ask_user` shows a reply box; answering it
  appends the reply to the transcript and resumes the run. A reply is
  refused while a tool approval is also pending on that task - approvals and
  replies stay on separate, explicit paths.
- Real failure semantics: three consecutive model responses that fail to
  parse as valid JSON fail the task (with the reason recorded and every bad
  step kept in the transcript); a response that parses successfully resets
  the counter. Any unhandled step error hands the task back for review
  instead of leaving it stuck in a `running` state nothing can act on.
- Native tool calling: when the configured model/provider supports it
  (OpenAI-compatible endpoints, llama.cpp, Ollama), the agent declares its
  tool set natively and consumes structured tool calls directly instead of
  parsing JSON out of prose; automatically falls back to the JSON protocol for
  models/providers without tool-calling support.
- Surgical file edits: `edit_file` (unique old_string/new_string replace) and
  `create_file` (new files only) alongside the existing whole-file
  `draft_patch`/`apply_draft_patch`; both approval-gated with the same
  workspace path containment as every other tool.
- Navigation tools: `glob_files` (`*`/`**` patterns), `search_files` with
  optional regex and context lines, `read_file` with an optional line range,
  and `list_files` with an optional subdirectory/depth.
- `set_plan`: a visible, agent-maintained plan checklist for multi-step goals;
  executes immediately since it only touches task state.
- `run_command` accepts template families with optional path/script arguments
  (`dotnet build`/`test [project]`, `npm test`, `npm run <script>` limited to
  scripts the workspace's own `package.json` declares, `cargo build`/`test`,
  `pytest [path]`), still gated by the workspace's own declared-safe recipes
  and always requiring approval. An identical repeat of an already-approved
  command string may auto-execute for the rest of that task.
- Agent self-learning: a per-machine lesson store records deterministic,
  evidence-backed observations from command results (structured exit codes;
  a timeout records nothing), patch outcomes, approval decisions (an
  approval of a previously-rejected action counters the rejection instead of
  being ignored), and task outcomes (a goal-fingerprint-keyed lesson on
  completion or failure, skipped for an uneventful success) - plus
  model-stated `[LESSON: ...]` observations. Dedupe signatures identify only
  the subject, never the outcome, so contradicting evidence actually lands
  on the same row instead of creating a permanently separate one; repeated
  evidence reinforces, contradiction decays and can retire/flip a lesson,
  and a task that completes successfully confirms every lesson that was
  actually shown to the model during it. Relevant lessons are injected into
  every step's context ranked by goal/tool-activity relevance (not raw
  confidence alone), and a Lessons panel supports manual edit/pin/retire/
  delete. Lessons only ever inform the model; they never change what the
  safety gate allows.
- Recent task and review queue lists are backed by a SQLite task index so large
  Agent workspaces do not need to scan every `task_state.json` file to render
  the queue.
- The agent panel surfaces a compact summary strip with task state, step
  count, goal, summary, recent task history, review queue counts, workspace
  memory counts, and retrieved context counts for quick scanning.
- The agent panel also shows a capability disclosure callout so users can see
  the current scope: local workspace inspection, approval-gated writes and
  commands, and no shell or network execution outside the fixed set.
- The agent panel also includes a workspace file browser with query, list,
  preview, and summary behaviour for faster inspection.
- Draft patch proposal UI: users can draft file edits with a rationale, propose
  content, and generate a side-by-side diff preview before queueing. The preview
  shows line-by-line changes with colour highlights (green for additions, red
  for removals) and line numbers for easy review in an approval modal. After
  approval, Aether writes the proposed content to the workspace file and
  refreshes the preview.
- Queued patches expander shows all draft patch decisions with rationale,
  proposed content, and outcome labels, allowing approve, reject, and block
  actions with explicit review metadata.
- The agent summary strip surfaces queued patch counts so approval work is
  visible at a glance without opening the lower panel, including pending and
  blocked counts.
- Workspace memory notes can be saved, reviewed, and deleted per workspace
  root.
- Workspace profile analysis can scan the selected root, detect project shape,
  languages, frameworks, important files, project instructions, risk notes,
  safe command recipes, a suggested `AGENTS.md`, and a RAG ingest plan. The
  result is saved back into workspace memory.
- No workspace root is selected by default: the agent never reads or
  analyzes any folder, and writes no workspace memory, until you explicitly
  choose one. Choosing a folder is what turns on file listing and workspace
  analysis for that root.
- Sub-task orchestration: `plan_subtasks` splits a broad, multi-domain goal
  into 2-6 focused sub-tasks (goal, specialist profile, success criteria),
  always approval-gated with a full preview of the proposed plan. Once
  approved, children run sequentially through the same loop, safety gate, and
  approval flow as any task, each with its own transcript, lessons, and
  remembered command approvals (never shared with siblings). Depth is limited
  to one level in code - a child cannot itself propose sub-tasks. A workbench
  strip shows live sub-task status; a child's pending approval surfaces in
  the review queue labeled with its parent's goal. Bounded by
  `Agent.MaxOrchestrationSteps` (default 60) across the whole run, separate
  from each child's own `Agent.MaxAutoSteps`; hitting it marks remaining
  sub-tasks `Skipped` and synthesis says so honestly. Once every sub-task is
  terminal, the parent synthesizes one consolidated report (with a
  deterministic fallback if synthesis itself fails) and writes it to
  `report.md` in the task directory, openable from the workbench.

## Model Management

- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
  Ollama chat streams incrementally like the other two providers, instead of
  buffering the full reply before the first token.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune that
  verifies GPU layer candidates before saving a per-GGUF tuned profile.
- GPU-aware llama.cpp builds. A runtime-variant setting (Auto by default)
  installs the CUDA build on NVIDIA hardware (with its `cudart` companion
  runtime), the Vulkan build on any other real GPU, and the CPU build when no
  GPU is detected; an explicit choice always wins. A GPU build that fails to
  launch falls back to the CPU build once. `GpuLayers = -1` offloads all
  layers (the default for new servers when a GPU is present), `0` stays on the
  CPU, and `N` offloads exactly N.
- Managed servers launch fast by default for a single user: one request slot
  (`--parallel 1`, so the whole context belongs to one conversation and every
  send reuses the KV cache), explicit prompt caching plus `--cache-reuse`, and
  a pinned embeddings batch pair that silences llama.cpp's clamp warning.
- Updating llama.cpp installs to the resolved install root instead of nesting
  a new version directory inside the previous one, preserves the configured
  runtime variant, notes that running servers keep the old build until
  restarted, and offers to prune superseded version directories (single
  confirm; current and previous kept).
- Managed Chat and Embeddings cards are normalized so duplicate default cards
  are removed. Starting a managed server stops any running peer on the same
  port first, allowing Chat and Embeddings to share a port when only one is
  active.
- Managed server processes (and the auto-tune probe and XTTS/Kokoro voice
  engines) join one Windows job object on launch, so they are killed by the
  OS if the app exits abnormally instead of surviving as an orphan holding a
  port and GPU memory.
- Starting a managed server checks the configured port first: a port already
  in use fails instantly and names the port and, best-effort, the owning
  process (PID and name), instead of launching a doomed process.
- If a leftover server process from a previous session is still holding a
  configured port, the Services view shows a banner naming it; a Stop button
  appears only when the process's executable exactly matches this server's
  configured binary (re-verified immediately before the process is killed).
  Any other process on the port is reported for information only.
- A managed server configured with a context size above 16384 shows an
  inline advisory in the Services view and a matching Doctor advisory: large
  KV caches can spill out of VRAM and slow prompt processing.
- Compare Models provides an in-chat practical comparison path for trying the
  same prompt across multiple visible models before choosing one for normal
  conversation.
- Model benchmarks with GGUF discovery, one-click full-suite runs, saved run
  history, deterministic quality checks, rankings that group runs by model,
  column headers and tabular display, test info modal for case details,
  reruns, and Markdown/JSON/CSV export. Rerun is disabled while a run is
  already in progress, so a second click can no longer interrupt and leak
  the first run's state.
- Benchmark views expose the test-details modal from both the per-result list
  and the best-run ranking rows, and saved benchmark history can be exported
  in bulk as one timestamped folder.
- Doctor checks for untuned GGUF files, stale `llama.cpp` binaries, and pinned
  `nomic-embed-text-v1.5` hash drift, with install actions where available.
- Doctor advises when a real GPU is present but inference is still configured
  for the CPU (a CPU-only build is installed, or the chat server offloads zero
  layers), naming the measured consequence and linking the fix in Services.
- The Models page lists every model as a compact, collapsed-by-default card
  (name, running badge, provider, size, tags, fits/update chips, tune
  summary) with a name/tag filter box, instead of a fully-expanded editor
  grid per model; expanding one shows the same editor as before. Mouse-wheel
  scrolling now works anywhere over the list, including over an expanded
  card's spinners.
- Per-model "Auto tune" button probes GPU layer candidates against the first
  configured managed `llama-server` executable and saves the result to the
  same tune-profile store the Services page uses, so a tune from either page
  shows up on both. "Auto-tune all" tunes every local GGUF that is not
  running and does not already have a fresh profile (missing, size/mtime
  drift, or a different llama-server build), sequentially and cancellable.
- Fits-on-your-hardware chips (Fits GPU / Partial offload / Too large) on
  Models-page cards, the Hugging Face browser's file list, and the setup
  wizard's recommended starter model, from a shared rough-headroom estimator
  and a process-lifetime-cached hardware snapshot.
- "Organize folder..." flattens a Hugging Face hub-cache layout
  (`hub\models--org--repo\snapshots\<sha>\*.gguf`) into a flat
  `Models\LLM\<file>.gguf` folder: plan, preview every move and any name
  collisions, confirm, then execute (same-volume rename or verified
  copy-then-delete across volumes). Never renames files, never overwrites a
  name collision, moves multi-part GGUF sets atomically, rewrites every
  settings reference to a moved file, and offers a separately-confirmed
  cleanup of folders left empty by the move.
- Local models can be linked to the Hugging Face repo they came from (via
  the folder organizer, the "Get models" browser, the starter-model
  download, or a manual "Link to Hugging Face repo..." action that validates
  the repo before saving). "Check for updates" compares each linked model's
  stored hash against the repo's current file hash (batched one HTTP call
  per repo); "Update" downloads, hash-verifies, and atomically swaps in the
  new file, flagging the card "re-tune recommended" afterward. A collapsed
  "Get models from Hugging Face" section on the Models page searches GGUF
  repos and downloads straight into the flat models folder. All Hugging
  Face access is anonymous, HTTPS-only, and manual-button-triggered - never
  on startup or a timer - and is disclosed in the Privacy Audit whenever a
  model is repo-linked.

## RAG

- Dataset Manager lists each dataset's chunk count, source count, embedding
  model, embedding dimensions, last ingest time, missing source files, stale
  local files, duplicate source/chunk entries, and reindex warnings when the
  current embedding model differs from the dataset metadata.
- Dataset metadata now records the embedding model and observed embedding
  dimensions during ingest so future reindex decisions are visible instead of
  implicit. The last ingest folder/URL list and timestamp are recorded from
  the very first ingest, not just re-ingests, and persist across restarts.
- **Reindex** action on a dataset card (shown when the embedding model
  differs) re-embeds every stored chunk with the current model, from stored
  content only, and rebuilds BM25 stats and the query cache. Adding documents
  to a dataset embedded with a different model is blocked with a message
  naming both models until you reindex. A cancelled or failed reindex leaves
  the dataset reporting its previous embedding model, so the reindex warning
  stays accurate instead of claiming a model change that never finished.
- **Add to dataset** pre-fills the target dataset's name for adding more
  documents to it. Editing that name box afterward (to create a different
  dataset instead) is honored - ingest then creates a new dataset under the
  edited name rather than silently adding to the original one.
- **Remove missing sources** action on a dataset card (shown when files are
  missing) lists the missing paths and, after confirmation, deletes their
  chunks and rebuilds BM25 stats and health. This is always an explicit,
  user-confirmed action; ingest never removes sources automatically.
- Parent-child chunking (a small embedded child size with a larger parent
  context size) now correctly returns child matches upgraded to parent
  content, instead of returning nothing.
- Deleting a dataset removes all of its chunks and BM25 stats explicitly, so
  nothing is left behind in the database.
- Re-ingesting into an already-queried dataset clears the in-memory query
  cache immediately, so new chunks are retrievable without restarting.
- RAG query refusal now checks retrieval strength (best semantic and BM25
  scores) instead of how much the question's wording overlaps the context, so
  differently-phrased questions that retrieval actually answered are no
  longer refused. A refusal still shows the closest sources it considered.
- Embedding input for a chunk now fits a full default-length chunk plus its
  header, instead of only the first roughly-quarter of it; an oversized
  custom chunk size setting surfaces an ingest health warning.

## Local AI Setup

- The first-run Setup Wizard now shows Kokoro onboarding details in the voice
  step, including the install plan and risk notes before you continue.
- Local AI setup scans can offer approval-gated downloads for a default Phi-4
  mini reasoning GGUF file (SHA256-verified) and a platform-specific
  `llama-server` binary when they are missing from the selected AI assets
  folder. The llama-server download fetches the real llama.cpp release
  archive for your platform, extracts it with a zip-slip guard, and locates
  the executable inside (Windows resolution also tries `.exe`, so a fresh
  Windows install's default managed servers are startable out of the box).
  Model downloads verify a SHA256 hash when Aether has trusted hash metadata
  for that exact URL.
- Local AI setup scans are voice-provider aware. Kokoro setup checks Kokoro
  Python imports and does not show XTTS script or model actions unless XTTS v2
  is selected.
- Generated XTTS helper scripts escape configured model/output paths before
  writing Python source, so unusual quotes or newlines in paths cannot alter the
  script body.
- The Local AI setup can now detect available GPU backends when creating
  a Python venv and will suggest a device (`cuda` for NVIDIA, `rocm` for
  AMD/ROCm, `mps` for Apple Silicon, or `cpu`) to use for TTS/model inference.
  You can still override the selected device in **Settings -> Voice providers**
  after setup.
- First-run Setup Wizard: on first launch Aether runs a guided 6-step
  setup wizard to select the data root, local AI assets root, chat backend,
  model folder, voice provider, and to run the Aether Doctor for a quick
  health check before you start using the app. The wizard can be skipped
  or re-run from the Settings panel.
- Finishing or skipping the wizard immediately starts configured servers and
  loads chat models, RAG datasets, and agent/benchmark data - no restart or
  extra navigation needed to make first use of the app work.
- Re-running the wizard and choosing a different data root moves your
  existing databases to the new location the same way Settings' data-root
  change does (with a confirmation toast), instead of switching to an empty
  root. A target folder that already has conflicting data files is refused
  with an explanation, and the current data root is left untouched.

## Aether Doctor

- Aether Doctor checks for storage, runtimes, voice, RAG, GPU, and secrets.
- Aether runs Doctor in the background after launch and raises a notification
  when errors or warnings are found, so startup problems are visible before the
  Doctor panel is opened.
- Aether Doctor now validates the configured Python and voice backend
  health before installs or playback.
- Doctor labels the Python check with the selected voice provider's actual
  requirement, such as Python 3.12+ for Kokoro or Python 3.9-11 for XTTS v2,
  and rejects an interpreter outside that range instead of accepting any
  newer minor version.
- Doctor only counts dedicated embedding GGUFs as embedding models, skips
  embedding backend health until one is installed, and leaves Linux global
  hotkeys out of problem reporting because system-wide support is not available
  there yet.
- Doctor embedding model install downloads the default model from a pinned
  Hugging Face commit, verifies SHA256, removes failed downloads, and points the
  embedding server at the verified file.
- Doctor flags a blank embedding endpoint (RAG's `EmbeddingBaseUrl`) while
  memory or RAG is enabled: embedding requests silently fall back to the chat
  server otherwise, queuing behind chat generation on a single-slot
  llama-server.
- Doctor reports whether the previous session exited cleanly, using a small
  local-only lifecycle journal (no telemetry, nothing leaves the machine).
  If Aether did not shut down cleanly last time (a crash or force-close), the
  warning names the last recorded operation, so a native-level crash that
  bypasses all managed error handling still leaves a starting point for
  diagnosis instead of no trace at all.

## System Integration

- App shutdown disposes the service container asynchronously so an
  async-only background service (like an active MCP session) no longer
  raises an unhandled exception on window close; a hung MCP child process is
  abandoned to the existing job-object cleanup after a bounded 5-second wait
  rather than blocking exit indefinitely.
- System overview for app version, CPU, RAM, storage, databases, managed
  components, and GPU/VRAM visibility. On Windows this now reports real
  available/total RAM (not the GC's view), an honest OS name and build
  (Windows 11 detected by build number, not the kernel string's misleading
  "10.0.x"), the marketing CPU name from the registry, and GPU name/VRAM
  from a registry fallback when `nvidia-smi` is not on PATH.
- Privacy Audit dashboard connects local-first posture into one view covering
  configured remote providers, local providers, network-facing managed server
  flags, secret backend health, runtime log redaction, data-root backup status,
  and features that may send data remotely.
- Runtime logs with filters, copy, and redacted diagnostics export.
- Runtime log entries are redacted before disk persistence and archive
  rotation avoids overwriting archives created in the same second. Redaction
  covers common API keys, bearer tokens, GitHub-style tokens, AWS-style access
  keys, Azure-style key assignments, password parameters, query-string secrets,
  and home paths.
- Tray integration, minimize-to-tray, local hotkeys, and Windows system-wide
  hotkeys.
- Single-instance guard: launching Aether while another instance for the
  same user account is already running exits immediately instead of opening
  a second window, since two processes would otherwise write to the same
  SQLite data root with no coordination between them.
- Toast notifications throughout the app with opaque popup backgrounds.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Data-root migration moves everything under the data root through one
  shared manifest (conversations, memories, benchmarks, Agent task state
  under `agent/`, secrets, trace/eval history, logs, the voice lexicon, and
  any future store), so migration, its preview, and backup can never
  disagree about what the data root contains.
- Local SQLite stores record schema versions and run additive migrations through
  a shared migration runner.
- Settings, small local state files, generated setup scripts, and export files
  use atomic replacement writes, and an unreadable `settings.json` is copied
  aside before defaults are loaded.
- Configurable local AI assets root for models, XTTS, venvs, and encoders.
  When both `Models` and `models` exist, Aether prefers the folder containing
  GGUF files for model and reranker defaults.
- Trust & Safety scan for configured local tools, hashes, AI-root scope, and
  network exposure warnings, including equals-style host override flags such as
  `--host=0.0.0.0`.
- Settings are implemented as domain sections for LLM defaults, RAG, data,
  local AI setup, voice, UI, memory, and trust while preserving one save flow.
- OS-backed secret references and redacted process logs.
- Local fallback secrets use an app-created per-data-root key file restricted
  to the current user, a random salt per encrypted value, and atomic vault
  writes. Backup excludes both the fallback vault and key file.
- Data-safety test harness for migration, backup/restore, and redaction.
- Backup restore rejects traversal and path-prefix escape entries before
  extraction, while still excluding the local fallback secret vault and key.
- Backup snapshots each SQLite database through SQLite's own online-backup
  API rather than zipping the raw file, so a backup taken mid-write is still
  internally consistent.

## Workbench Glue

Aether connects its local-first systems around a workspace root so chat, RAG,
agent state, project instructions, and local safety checks can share context.

### Workspace Profiles

The Agent workspace now records a profile around a project root:

- Root folder
- Preferred chat model
- Linked RAG dataset
- Workspace memory count
- Trust and safety status
- Last workspace summary

Planned profile fields include preferred embedding model, recent chats,
benchmark history, and richer trust status.

### Workspace Understanding

An **Explain Workspace** action scans the selected root and saves a summary into
workspace memory. The scan identifies repo type, languages, frameworks, safe
build and test command recipes, important files, risks, a suggested
`AGENTS.md`, and a RAG ingest plan.

### Project Instructions

The Project Instructions view detects local instruction sources, summarizes the
active guidance, and flags conflicts or risky override language. Candidate files
include:

- `AGENTS.md`
- `CLAUDE.md`
- `GEMINI.md`
- `.codex/instructions.md`
- `.github/copilot-instructions.md`
- `README.md`
- `CONTRIBUTING.md`

### Context Transparency

The Agent panel already exposes retrieved context, task state, next action, and
workspace analysis outputs.

Chat now includes an opt-in Context Inspector before send. It shows the system
prompt, draft message, ready attached files, raw prompt sections, and estimated
tokens for the current context pack.

Chat Trace Viewer now extends the current RAG and agent trace model with
selected model/runtime, system prompt, attachment count, token estimate,
provider usage, latency, and error details for completed sends.

Conversations can be exported from chat as Markdown or JSON. Markdown includes
conversation metadata, the system prompt when present, role-separated messages,
model IDs, error or incomplete markers, and attached file paths. JSON preserves
the stored conversation shape for local migration or inspection.

Chat now injects relevant stored memories into each turn's system prompt when
Memory is enabled (Settings > Memory), and shows which memories were actually
used as a small Sources panel under the assistant's reply, each with the
memory's content as a tooltip. This closes a gap from the original memory
feature: memory injection existed as a service but nothing in chat ever
called it.

### Model and Dataset Lifecycle

Compare Models sends the same prompt to one to four selected models and shows
answer quality notes, latency, token usage, and errors.

A RAG Dataset Manager shows dataset source count, chunk count, embedding model,
dimensions, last ingest time, stale files, missing files, duplicate sources, and
reindex warnings when the current embedding provider differs from the one used
to create the dataset.

### Local-First Operations

The System Overview Privacy Audit dashboard connects existing trust, secrets,
logs, and runtime checks into one view covering remote providers, local-only
providers, features that may send data remotely, exposed local servers, secret
health, log redaction status, and data-root backup status.

Planned work such as a Doctor Fixes queue and safe command recipe cards is
tracked in [docs/review/03-next-level-roadmap.md](review/03-next-level-roadmap.md),
not documented here as existing behaviour.

### Local API

An optional, off-by-default loopback HTTP host (`Aether.LocalApi`) that lets
other local processes (editor extensions, scripts) reuse Aether's chat,
memory, and RAG query surface without the desktop UI. Enabled and configured
from Settings > Local API: a checkbox, a port (127.0.0.1 only), and any
number of named per-app bearer tokens (add one, name it, copy the generated
value; revoke any one individually without affecting the others). The host
refuses every request with a 503 until at least one token exists. Settings
shows a live host status label (Running/Stopped/etc.) next to the checkbox.
Every call is logged to the shared trace store keyed by the verified token
name that authenticated it (the caller-supplied `X-Aether-Client` header is
also recorded, but only as an unverified display hint), and Privacy Audit's
"Local API activity" item shows which per-app tokens have been calling in.

Endpoints:
- `POST /v1/chat/completions` - buffered JSON by default; pass `"stream": true`
  for Server-Sent Events in the OpenAI `chat.completion.chunk` wire shape
  (compatibility with existing SSE clients, not a dependency on OpenAI).
  Accepts the same sampling parameters as the desktop app (temperature, top P,
  top K, min P, repeat/frequency/presence penalty), applying the same
  precedence the desktop Chat panel uses: explicit request value, then the
  model's saved profile default, then the global LLM setting.
- `POST /v1/embeddings` - one vector per input string, using the app's
  configured embedding provider.
- `GET /v1/memory/query`, `POST /v1/rag/query`, `GET /v1/models`.
