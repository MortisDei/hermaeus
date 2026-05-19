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
  reported, first-token latency, total latency, and error details.
- Compare Models sends the current draft prompt to one to four selected models
  and compares answers, latency, token usage, and simple quality notes without
  adding the comparison run to chat history.
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
- Dedicated Memories panel to review, search, pin, archive, and delete
  memories.
- Session Usage panel: view per-conversation memory counts and recent activity
  to help triage which conversations have stored memories.
- Configurable memory controls in Settings: global enable, context injection,
  auto-summary threshold, token budget, encryption toggle, and auto-archive
  days.
- Post-response auto-summary pipeline: important conversations are analysed in
  the background and durable memories are extracted and merged with
  deduplication safeguards.
- Chat header now shows a live memory status line so you can see whether memory
  is enabled and how many recent memories are available.

## Agent Workbench

- Read-first local task workbench with explicit task state, compact context
  packs, local logs/traces, and review queue controls.
- Recent task and review queue lists are backed by a SQLite task index so large
  Agent workspaces do not need to scan every `task_state.json` file to render
  the queue.
- The agent panel surfaces a compact summary strip with task state, goal,
  summary, recent task history, review queue counts, workspace memory counts,
  and retrieved context counts for quick scanning.
- The agent panel also shows a capability disclosure callout so users can see
  the current slice: read-first workspace inspection, approval-gated patch
  drafting, and no shell or network execution.
- The agent panel also includes a workspace file browser with query, list,
  preview, and summary behaviour for faster read-first inspection.
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

## Model Management

- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune that
  verifies GPU layer candidates before saving a per-GGUF tuned profile.
- Compare Models provides an in-chat practical comparison path for trying the
  same prompt across multiple visible models before choosing one for normal
  conversation.
- Model benchmarks with GGUF discovery, one-click full-suite runs, saved run
  history, deterministic quality checks, rankings that group runs by model,
  column headers and tabular display, test info modal for case details,
  reruns, and Markdown/JSON/CSV export.
- Benchmark views expose the test-details modal from both the per-result list
  and the best-run ranking rows, and saved benchmark history can be exported
  in bulk as one timestamped folder.
- Doctor checks for untuned GGUF files, stale `llama.cpp` binaries, and pinned
  `nomic-embed-text-v1.5` hash drift, with install actions where available.

## RAG

- Dataset Manager lists each dataset's chunk count, source count, embedding
  model, embedding dimensions, last ingest time, missing source files, stale
  local files, duplicate source/chunk entries, and reindex warnings when the
  current embedding model differs from the dataset metadata.
- Dataset metadata now records the embedding model and observed embedding
  dimensions during ingest so future reindex decisions are visible instead of
  implicit.

## Local AI Setup

- The first-run Setup Wizard now shows Kokoro onboarding details in the voice
  step, including the install plan and risk notes before you continue.
- Local AI setup scans can offer approval-gated downloads for a default Phi-4
  mini reasoning GGUF file and a platform-specific `llama-server` binary when
  they are missing from the selected AI assets folder. Model downloads verify a
  SHA256 hash when Aether has trusted hash metadata for that exact URL.
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

## Aether Doctor

- Aether Doctor checks for storage, runtimes, voice, RAG, GPU, and secrets.
- Aether runs Doctor in the background after launch and raises a notification
  when errors or warnings are found, so startup problems are visible before the
  Doctor panel is opened.
- Aether Doctor now validates the configured Python and voice backend
  health before installs or playback.
- Doctor labels the Python check with the selected voice provider's actual
  requirement, such as Python 3.12 for Kokoro or Python 3.11 for XTTS v2.
- Doctor only counts dedicated embedding GGUFs as embedding models, skips
  embedding backend health until one is installed, and leaves Linux global
  hotkeys out of problem reporting because system-wide support is not available
  there yet.
- Doctor embedding model install downloads the default model from a pinned
  Hugging Face commit, verifies SHA256, removes failed downloads, and points the
  embedding server at the verified file.

## System Integration

- System overview for app version, CPU, RAM, storage, databases, managed
  components, and best-effort GPU/VRAM visibility.
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
- Local tasks, reminders, and app-running scheduled automations.
- Tray integration, minimize-to-tray, local hotkeys, and Windows system-wide
  hotkeys.
- Toast notifications throughout the app with opaque popup backgrounds.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Data-root migration includes Agent task state and workspace memory under
  `agent/`.
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

A Doctor Fixes queue should turn Doctor findings into explicit approval-gated
actions with target paths, command previews, risk notes, and recorded outcomes.

Safe command recipe cards should suggest manual commands, explain why they are
useful, classify risk, and provide copy controls. This keeps early agent work
safe while leaving a clear path toward approval-gated execution later.
