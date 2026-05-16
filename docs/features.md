# Features

## Chat

- Chat history with rename, delete, fast FTS-backed search, folders, tags,
  pins, archive, and direct file context injection for selected text/code files.
- Chat context usage indicator with provider-reported usage where available and
  local estimates before send.
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
- The agent panel surfaces a compact summary strip with task state, goal,
  summary, recent task history, review queue counts, workspace memory counts,
  and retrieved context counts for quick scanning.
- The agent panel also shows a capability disclosure callout so users can see
  the current slice: read-first workspace inspection, approval-gated patch
  drafting, and no shell or network execution.
- The agent panel also includes a workspace file browser with query, list,
  preview, and summary behaviour for faster read-first inspection.
- Draft patch proposal UI: users can draft file edits with a rationale, propose
  content, generate a formatted patch preview, and queue the patch for approval
  in the task review queue. Approving a patch writes the proposed content to
  the selected workspace file and refreshes the file preview.
- Queued patches expander shows all pending draft patches with rationale and
  proposed content, allowing approve/reject actions that track approval state
  and metadata (approver, approval timestamp).
- The agent summary strip surfaces queued patch counts so approval work is
  visible at a glance without opening the lower panel.
- Workspace memory notes can be saved, reviewed, and deleted per workspace
  root.

## Model Management

- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune.
- Model benchmarks with saved run history, deterministic quality checks,
  rankings, reruns, and Markdown/JSON/CSV export.

## Local AI Setup

- The first-run Setup Wizard now shows Kokoro onboarding details in the voice
  step, including the install plan and risk notes before you continue.
- Local AI setup scans can offer approval-gated downloads for a default Phi-4
  mini reasoning GGUF file and a platform-specific `llama-server` binary when
  they are missing from the selected AI assets folder.
- The Local AI setup can now detect available GPU backends when creating
  a Python venv and will suggest a device (`cuda` for NVIDIA, `rocm` for
  AMD/ROCm, or `cpu`) to use for TTS/model inference. You can still override
  the selected device in **Settings → Voice providers** after setup.
- First-run Setup Wizard: on first launch Aether runs a guided 6-step
  setup wizard to select the data root, local AI assets root, chat backend,
  model folder, voice provider, and to run the Aether Doctor for a quick
  health check before you start using the app. The wizard can be skipped
  or re-run from the Settings panel.

## Aether Doctor

- Aether Doctor checks for storage, runtimes, voice, RAG, GPU, and secrets.
- Aether Doctor now validates the configured Python and voice backend
  health before installs or playback.

## System Integration

- System overview for app version, CPU, RAM, storage, databases, managed
  components, and best-effort GPU/VRAM visibility.
- Runtime logs with filters, copy, and redacted diagnostics export.
- Local tasks, reminders, and app-running scheduled automations.
- Tray integration, minimize-to-tray, local hotkeys, and Windows system-wide
  hotkeys.
- Toast notifications throughout the app.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Configurable local AI assets root for models, XTTS, venvs, and encoders.
- Trust & Safety scan for configured local tools, hashes, AI-root scope, and
  network exposure warnings.
- OS-backed secret references and redacted process logs.
- Data-safety test harness for migration, backup/restore, and redaction.
