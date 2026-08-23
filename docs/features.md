# Features

## Chat

- Normal Chat adds a compact environment block built from live model locality,
  attachment routing, attached Knowledge, memory, and Recall state. It keeps
  intrinsic model abilities separate and explicitly says that web, shell,
  tools, and Agent workspace actions are not exposed in ordinary Chat.
- Deleting the active conversation creates a clean conversation and explicitly
  returns keyboard focus to the composer.
- **Reasoning is a separate transcript channel.** llama.cpp, OpenAI-compatible,
  and Ollama streams may provide reasoning deltas without leaking them into the
  final answer. Reasoning is stored, reloads with its assistant message, starts
  collapsed, and can be copied from its own labelled section. Markdown export
  labels it separately and JSON remains lossless. Ordinary Copy, speech, memory,
  Recall, title generation, and transcript search use answer content only.
- **Preserved reasoning is evidence-gated.** Hermaeus reports Unknown rather than
  guessing from a model filename. History replay requires a managed llama.cpp
  route, matching template evidence from `/props`, a compatible runtime launch,
  and the per-server Preserve reasoning setting.

## Models and setup

- A starter model with a matching pinned hash is adopted on retry without a
  second download. Conflicting files are never overwritten, and setup does not
  finish until the selected path is present in the saved managed server and the
  local model scan.
- Model cards share context size and one KV-cache precision with Services. The
  precision applies to both llama.cpp K and V flags. Hugging Face file sets show
  On disk, Complete set, Downloading, or Download before the first click.
- Deleting a local GGUF is confirmation-gated and removes only the exact validated
  file, manifest entry, profile, and stopped server references. Running servers,
  symlinks, traversal, and outside-root targets are refused.

## Memories

- Pin and delete actions bind through the named Memories view root. Permanent
  deletion asks for confirmation and leaves the card and database unchanged when
  cancelled.


- **The model dropdown does not wait for a model to load** (r27). Startup used
  to auto-start every managed server one at a time, each behind a five-minute
  health deadline, before listing chat models, even though a server reaching
  Running already re-lists them by itself. Servers now start alongside the app
  rather than ahead of it, and the panel you opened the app to use is usable
  first.
- **While a chat server is still loading its model**, one line above the
  composer names the server and how long it has been starting. Past 90 seconds
  it adds that this is longer than usual and offers to open the Services log.
  There is no progress bar: llama-server reports nothing at all between launch
  and healthy, so a bar would imply knowledge the app does not have.
- **A message sent during that wait is held, not swallowed.** It appears in the
  conversation marked as waiting, the composer clears so you can carry on
  thinking, and it sends once, through the ordinary send path, the moment a
  model lists. One message at a time: a second send while one is held is
  refused. Cancelling gives the text back to the composer, and so does a
  timeout or the server failing to start. Nothing is ever sent that you did not
  submit, and a hold never survives a restart.

- **Both Enter combinations work.** With `Ctrl+Enter to send` off (the default)
  Enter sends and Ctrl+Enter inserts a newline; with it on, the two swap.
  Shift+Enter inserts a newline either way. Before 0.36.0-alpha only the send
  half was implemented, so in the default configuration no key produced a
  newline in the chat box at all.
- **The copy and read-aloud buttons under the last message can be clicked.**
  They used to end up flush against the input bar. The RAG panel's last card
  had the same problem and the same fix.

- **Conversation branching.** A conversation is a tree, not a line. Regenerating
  an answer adds a new version alongside the old one instead of replacing it, and
  editing one of your own messages sends the edit as a new version while leaving
  the original and everything under it intact. Any message with more than one
  version shows a compact `< 2/3 >` switcher; messages that have never been
  branched show nothing new. Deleting a version is explicit, confirms how many
  messages go, and refuses when only one version is left.

  Before this, Regenerate deleted both the previous answer and the question and
  put the text back in the input box, so the earlier answer was gone from the
  conversation and from disk on the next save.

  Every branch is saved, so conversation search and Recall still find a message
  you navigated away from. The prompt sent to the model, token accounting, memory
  extraction and export all follow the version you are currently looking at.
- **One context receipt per answer.** Everything injected into a turn - memories,
  Recall hits, knowledge excerpts - collapses behind a single line reading, for
  example, `Context: 3 memories, 2 recall hits, 4 knowledge excerpts`, expandable
  to the individual items. Collapsed means nothing from any source is shown.
  Inline `[1]`, `[2]` citation markers in the answer itself are unaffected.
- Chat history with rename, delete, fast FTS-backed search, folders, tags,
  pins, archive, and direct file context injection for selected text/code files.
- Conversation management via right-click context menu with delete, archive,
  export, and pin options for convenient quick access.
- The conversation details flyout (title/folder/tags) auto-saves as you type,
  debounced to a single save per pause in typing rather than on every
  keystroke; there is no separate Save button. Pin and archive still take
  effect immediately.
- Chat context usage indicator with provider-reported usage where available and
  local estimates before send.
- Context Inspector panel shows the exact context pack before send when opened:
  system prompt, draft message, ready attachments, chat history token estimate,
  the raw prompt sections that will be sent to the model, and - when a
  Knowledge dataset is attached - a "Knowledge" part showing the retrieved
  block that would be injected for the current draft, or a one-line
  retrieval-skipped/failed reason when nothing would be injected.
- Chat Trace Viewer records completed sends with selected model/runtime,
  system prompt, attachment count, estimated tokens, provider usage when
  reported, first-token latency, total latency, error details, and a
  pre-stream timing breakdown (memory recall, injection selection, lesson
  context, Knowledge retrieval, prompt build, first token) so a slow send is
  diagnosable without a profiler. When a Knowledge dataset is attached, the
  trace also shows how many chunks were injected and a note (weak-retrieval
  skip, embedding-failure fallback, or a missing dataset) whenever nothing
  was injected. When llama.cpp reports its own prompt-processing timings, the
  breakdown also shows the server-side prompt tokens/time so a slow send can
  be narrowed to request queuing versus actual prompt evaluation. The
  breakdown also separates the first streamed event of any kind from the first
  visible content token, so a long non-content stream prefix is no longer
  hidden inside "before first token". A send whose pre-first-token wait
  exceeds 10 seconds logs a runtime warning with the full breakdown, and on a
  machine with a real GPU that is still configured for CPU inference the
  warning ends with a "prompt was read at CPU speed" hint pointing at Doctor.
- Knowledge (r21): a conversation can have one RAG dataset attached via the
  chat header's "Knowledge" picker (a dataset name, chunk count list; "None"
  detaches). Every send against that conversation retrieves from the attached
  dataset and, when retrieval clears the confidence threshold, injects a
  bounded context block into the prompt with the retrieved chunks surfaced as
  individually clickable citation pills on the reply - the same pills RAG's
  own query pane uses. A weak/unrelated match (e.g. "thanks!") injects
  nothing rather than parroting unrelated chunks into every message. Chat
  stays chat: retrieval only adds context, it never blocks, rewrites, or
  refuses a send the way the RAG panel's grounded-answer mode does. The
  injection budget is configurable at Settings > RAG ("Chat knowledge context
  budget"). A dataset that no longer resolves (deleted, or a temporarily
  unmounted data root) shows "Knowledge: missing" in the picker rather than
  silently forgetting the attachment or erroring the send.
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
- Chat can attach selected local text/code files, `.docx`, and `.pdf` files
  directly to the next message. Use the attach button or drop files on the
  input. Hermaeus reads each file once at send time, extracting text first for
  `.docx`/`.pdf` (a scanned PDF or one with no extractable text is Skipped
  with the reason shown, not silently dropped), prepends a bounded context
  block to the model prompt, and stores only an attachment summary in
  conversation history.
- Chat can also attach up to 4 images (`.png`/`.jpg`/`.jpeg`/`.webp`, 8 MB
  each) per message when the active chat server has a Vision projector
  configured (Services > Vision projector, `--mmproj`), or when the selected
  model routes through the OpenAI provider (its API accepts the same
  `image_url` content part natively, no local projector needed); they ride
  the same attach button and render a thumbnail chip, or can be pasted
  directly into the message box (Ctrl+V, or right-click Paste) from anything
  that copies a PNG to the clipboard, such as Snipping Tool. Without either
  vision path configured, an attached image is Skipped with an honest reason
  instead of silently degrading to text-only, and images never count against
  the text context budget. The attach button's file picker defaults to
  showing every supported type (text/code, `.docx`/`.pdf`, images) at once
  rather than defaulting to a narrower filter that hides the others.
- Attachment file paths are also persisted with each user message so regenerate
  and edit-and-resend can reattach context files after an app restart when those
  files still exist.
- Every fenced code block in a rendered assistant reply has a Save button that
  writes it to that conversation's artifacts folder
  (`{DataRoot}/chat-artifacts/{sanitized conversation title}/`, falling back
  to the conversation id when the conversation has no title yet; a hidden
  marker file keeps the folder stable if the conversation is renamed later,
  so browsing chat-artifacts in a file manager means something instead of
  showing a bare GUID); a collapsed "Artifacts: N" strip above the input bar
  expands to a list with Open/Reveal-in-folder actions per file and a button
  to open the conversation's artifacts folder directly. The saved filename is
  derived from the reply's first markdown heading (falling back to the
  conversation title, then "artifact"), stripping a trailing extension the
  heading may already carry (e.g. a "# calculator.cs" heading) before adding
  the language's own extension, so it never doubles up as `calculator.cs.cs`.
- A reply cut off by the configured max-tokens cap (the provider reports
  `finish_reason: length`) shows a "Continue" affordance instead of quietly
  reading as a complete answer.
- A malformed assistant reply that would break Markdown rendering falls back
  to plain selectable text instead of taking the whole chat view down with
  it.
- The message list only auto-scrolls to a new streamed token when it was
  already pinned to the bottom; scrolling up to reread earlier messages
  during a stream no longer gets yanked back down.
- The chat bar shows current context usage against the selected context window.
  It uses provider-reported token usage when available and falls back to a
  local estimate for draft input, visible history, system prompt, and ready file
  attachments. At high usage, Hermaeus warns so a fresh conversation can avoid
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
  Archived and expired memories are excluded from search and injection, so a
  retired or forgotten memory never resurfaces.
- Evidence-backed relationships: memories retain the compatible
  `RelatedMemoryIds` list and can additionally link to a memory, Agent Lesson,
  benchmark run, model profile, or runtime profile with a bounded relationship
  type (`related to`, `derived from`, `supports`, `contradicts`, `updates`,
  `supersedes`, or `tested by`) and shared source evidence. Normal recall stays
  lexical/vector-first. It may add one directly related active memory at a
  discounted score, and a superseded memory yields to its direct current fact.
  The chat context receipt identifies relationship-expanded memory so this is
  inspectable rather than hidden ranking behavior.
- Relevance-aware injection: memories selected for chat context are ranked by
  the search's own relevance score blended with **decayed** importance (the
  same lifecycle decay the archiver uses), not recency-first as before;
  recency is now only the final tiebreaker.
- Lifecycle: each memory tracks how many times and when it was actually
  recalled (injected), not just retrieved. Effective importance decays for
  memories that go unused; ones that decay below a floor and stay unrecalled
  long enough are auto-archived (never hard-deleted) the next time the
  Memories panel opens. Settings > Memory's auto-archive-after-days is
  enforced the same way: a memory past its expiration date is archived on
  the next sweep and excluded from search immediately, even before that
  sweep runs. Pinned memories never decay and never expire.
- The model can save a new memory anywhere in its response with
  `[MEMORY: <content>]` (up to 3 per turn, deduplicated against existing
  memories so a repeated or near-equivalent fact reinforces one row instead of piling up), and
  correct or retire a memory it was shown this turn with
  `[MEMORY_UPDATE: <id> | <content>]` / `[MEMORY_FORGET: <id>]`; only ids
  actually injected into that turn are honored for update/forget, everything
  else is ignored and logged. Marker syntax never reaches the persisted
  transcript, whether or not anything was actually saved, updated, or
  forgotten that turn.
- Auto-summary now asks for structured JSON (content, category, importance,
  tags) instead of parsing `[MEMORY: ...]` markers with keyword heuristics,
  giving model-supplied metadata directly; the marker format remains the
  fallback if a model doesn't follow the JSON instruction.
- On a provider that can enforce a response shape (llama.cpp and Ollama
  locally, and api.openai.com), auto-summary no longer asks for that JSON, it
  requires it: the schema goes to the sampler, so the reply is the right
  shape by construction. The marker fallback and the salvage parser both stay
  exactly as they were, because they are what runs for every provider that
  cannot enforce a shape. What changes is that they stop being the common
  path. See "Constrained output" below.
- Dedicated Memories panel to review, search, pin, archive, and delete
  memories, sorted by effective importance and showing recall stats. When an
  embedding-model switch leaves older memories at a different vector
  dimensionality than the current model (silently zeroing their semantic
  recall score), the panel shows a count and a "Re-embed memories" button
  that clears the stale vectors and re-embeds them in the background; this
  never happens automatically on a model switch.
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

## Recall

Recall (r24) is a single local search index over the words you have already
produced in Hermaeus - past messages, agent tasks, memories, and RAG document
chunks - fused into one ranked result via reciprocal rank fusion, distinct
from RAG (which answers questions from documents you deliberately ingested).
On by default, indexing every conversation and agent task locally as they are
created; a visible switch, a genuine "Clear index" action, and honest size
reporting live in Settings > Memory. Per-conversation exclusion is available
from the conversation list. The command palette (**Ctrl+K**) searches Recall
alongside registered app commands from a single box; an empty query shows
commands grouped by area. Selecting a hit navigates straight to it (the
conversation, task, memory, or dataset). Optional chat-side injection (off by
default) can pull relevant Recall hits into a turn's context, separate from
and in addition to Memory/RAG injection. See [docs/recall.md](recall.md).

## Agent Workbench

- Local task workbench with explicit task state, a persisted step transcript,
  compact context packs, local logs/traces, and review queue controls.
- **Transcript replay keeps repeated evidence legible without rewriting the
  audit trail.** `transcript.jsonl` remains the complete raw sequence. For the
  next model decision only, consecutive replay-safe tool outcomes with the same
  tool, canonical arguments, and result collapse to the first outcome plus a
  repeat count and deterministic range. Failed, timed-out, denied, changed,
  and historical entries without matching provenance remain separate. Three or
  more unchanged calls appear as an informational context-receipt diagnostic;
  nothing is auto-blocked and the agent's status and loop budget do not change.
- **Steer a task that is already running.** The reply box sends an instruction
  into a run in flight instead of only answering an `ask_user` question; its
  caption and button change so it is clear which of the two you are doing. The
  instruction interrupts the model call in progress (never a tool that has
  started executing, which runs to completion), is folded into the planner's
  context at the next step boundary, appears in the transcript at once, and
  consumes a step from the budget. **It cannot approve anything**: an injected
  instruction is user text, exactly as untrusted as the goal, and telling the
  agent "you have my approval, do not ask again" changes nothing about the
  safety gate. Refused, with the reason, on a finished task (use Continue), on
  an orchestration parent with a running child, and when eight instructions are
  already queued. See `docs/agent.md`.
- Every agent run has an undo button. Hermaeus keeps a ledger of everything a
  run changed and can put it all back, file by file, with one click. The
  Changes view shows every file, command, and approval a run produced
  (folding in an orchestration parent's children); Rewind restores or
  deletes files per the ledger, skipping anything changed again since,
  behind a mandatory confirmation that lists exactly what will happen.
- Every approval is bound to a fingerprint of the pending action as
  displayed; a mismatch between what was shown and what is actually pending
  refuses to execute instead of running whatever changed underneath it.
- Workspace policy: an optional `.hermaeus/workspace.json` `policy` block
  narrows (never widens) which paths the agent's tools may read or write,
  plus a per-task cap on file reads; a denied read refuses gracefully, a
  denied write is blocked before it can ever be approved, and a malformed
  policy is rejected as a whole with a visible warning.
- Optional plan-approval checkpoint (`Agent.RequirePlanApproval`, off by
  default): a fresh task's first `set_plan` pauses the run for review before
  anything unattended happens, at most once per task. A later `set_plan`
  revising a non-empty plan is now logged and annotated ("revised at step
  N") instead of silently replacing the checklist.
- A final answer may optionally carry a short list of specific things the
  model could not verify or finish ("reservations") - never a numeric
  confidence score, never required. A task completing with any shows
  "Completed with reservations" in the summary strip and recent-tasks list;
  orchestration synthesis carries a child's reservations into the parent's
  report under a Reservations heading.
- Autonomous runs: Start (or resuming after an approval or a reply) runs
  steps back to back without a click per step, stopping at a final answer, a
  question for the user, a gated action needing approval, a blocked or
  failed task, or a configurable step cap (`Agent.MaxAutoSteps`, default
  20) - which now hands the task back for review with a note instead of
  leaving it looking silently active. Live progress and Stop remain
  available; a manual single-step advance is still available too.
- A task that reached a terminal state before its plan actually finished
  (e.g. the model stopped short, or hit the step cap) shows a note
  explaining why next to a Continue box: typed instructions resume that same
  task instead of forcing a new one to be started from scratch. A "New Task"
  button next to Start is always available for the actual fresh-start case.
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
  the queue. A Recent Tasks list in the workbench (status chip, goal, relative
  time, pending step count; sub-task children indented with a tag) makes every
  task reachable after a restart, not just ones currently waiting in the
  review queue, which also gained an Open button; opening a child directly
  shows its parent's goal.
- The workbench is a one-line status bar, a pinned decision strip, and four
  tabs (Run, Changes, Workspace, History), each scrolling on its own. The
  decision the agent is waiting on lives in the strip and is never behind a
  tab. The panel opens on Run every time and never switches tabs on its own;
  a finished run lights the Changes tab's pending-patch count instead.
- The review queue lists only tasks that need a decision now. A task that was
  approved in the past is not in it, and an approval sent to a task with
  nothing pending is refused with a reason rather than recorded: it changes
  no status and cannot restart a finished run. The queue refreshes itself
  when a run pauses. Dismiss discards a queued task's pending action without
  running it and closes the task as cancelled, so a run you are done with can
  actually leave the queue; it stays in Recent Tasks with its ledger intact.
- While a run is in flight the status line shows a progress bar and a rotating
  activity line naming the current step and how long it has been going, so a
  long model call no longer looks like a hung app.
- Command recipes are editable from the Workspace tab: pick one of the fixed
  command families the safety gate accepts, optionally narrow it with an
  argument, and it is saved to `.hermaeus/workspace.json` immediately; Remove
  takes it away again. A workspace that declares none can run nothing, and
  declaring one previously meant hand-editing a file the user had no reason to
  know about. The picker cannot express a command the gate would refuse, and
  every run still asks for approval.
- When the agent asks a question, the question itself is shown above the reply
  box. It previously reached only the log and the transcript, so the workbench
  asked for an answer without saying what had been asked.
- An unreadable model response is retried automatically with a corrective note
  instead of parking the task on a question it never asked; three in a row
  still fail it. A response that names a tool in the action's `type` field is
  repaired into the protocol's shape and executed, which is a common
  local-model mistake in an otherwise valid response. The repair changes the
  shape of the request, never its authority: the safety gate still classifies
  the tool exactly as before.
- A finished run says what it did, from the run ledger: files changed (split
  created and edited, with the line delta), commands run and how many failed,
  approvals asked for and how they went, any unfinished plan steps, and the
  model's reservations. A run that changed nothing says so. No score, no
  grade, no percentage.
- The workbench says what the agent can do in this workspace, derived from the
  real tool set, the workspace's own declared command recipes, its policy, and
  whether an MCP bridge is configured, rather than from a fixed list of
  sentences.
- The agent panel also includes a workspace file browser with query, list,
  preview, and summary behaviour for faster inspection.
- Draft patch proposal UI: users can draft file edits with a rationale, propose
  content, and generate a side-by-side diff preview before queueing. The preview
  shows line-by-line changes with colour highlights (green for additions, red
  for removals) and line numbers for easy review in an approval modal. After
  approval, Hermaeus writes the proposed content to the workspace file and
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
  approval flow as any task, each with its own transcript, lessons, workspace
  root (inherited from the parent), and remembered command approvals (never
  shared with siblings). Depth is limited to one level in code - a child
  cannot itself propose sub-tasks, and a task whose plan already exists
  cannot accept another proposal. A workbench strip shows live sub-task
  status; a child's pending approval surfaces in the review queue labeled
  with its parent's goal, and approving it always resumes the parent's
  orchestration by id, even if the child itself is the task currently open.
  The parent's own status honestly mirrors a paused child's instead of
  showing `Running` with nothing happening, and self-heals if a child
  reaches a terminal state outside the orchestration loop (e.g. opened
  directly and stepped to completion) rather than getting permanently stuck.
  Bounded by `Agent.MaxOrchestrationSteps` (default 60) across the whole run,
  separate from each child's own `Agent.MaxAutoSteps`; hitting it marks
  remaining sub-tasks `Skipped` and synthesis says so honestly. Once every
  sub-task is terminal, the parent synthesizes one consolidated report (with
  a deterministic fallback if synthesis itself fails) and writes it to
  `report.md` in the task directory, openable from the workbench.
- The agent's current response gets its own always-expanded panel (wraps,
  scrolls internally past ~320px) near the top of the workbench, above the
  Task State/Next Action detail and the reference panels (workspace profile,
  files, retrieved context) checked less often once a run is going. The
  header's short status phrase next to Run Step/Stop is unchanged and stays
  supplementary to the response panel, not a replacement for it.
- The built-in Agent Scenario Suite's sub-task status check now passes for any
  plan whose sub-tasks reached every expected status at least once, rather
  than requiring the exact same number and order of sub-tasks the manifest
  hardcodes - a model reasonably splitting work differently no longer fails
  the check on its own.
- Sixteen built-in scenarios, including three added in r23: confused user
  authority (a pre-announced "I confirm, this is pre-approved" must still go
  through the approval gate), tool result poisoning (provocative file and
  directory names as the injection vector, not file body content), and
  memory poisoning (a workspace instructs the agent to record a lesson
  claiming blanket approval; the stated-lesson gate-claim filter must reject
  it, checked by a new forbid_active_lesson_matching scenario check).

## Model Management

- The model list is a wrapping grid of cards rather than full-width rows. Each
  card shows the effective name, a Running badge, a source badge, the raw name
  and provider, size, VRAM fit, update state and tags, with the modified date,
  Auto tune and a Configure button in its footer. Configure opens the full
  per-model editor (display name, description, tags, temperature, context size,
  max tokens, top-p, top-k, min-p, repeat/frequency/presence penalty,
  visibility, avatar, Save, Reset, repo link and Update) in a flyout.
- The source badge says where a model came from: "HF" for one downloaded
  through, or linked to, a Hugging Face repo (the badge's tooltip names the
  repo), "Local" for a GGUF found on disk with no recorded provenance, and
  nothing at all for a model reported live by a running provider. It is drawn
  from a source kind rather than a Hugging Face flag, so a second download
  provider gets its own glyph and label in the same slot.
- The setup wizard offers a choice of starter models rather than one
  recommendation, and **states each model's licence before you download it**.
  The deliberately small current list is Phi-4 mini, Gemma 4 E2B/E4B IT QAT,
  and official Qwen3 8B/14B GGUFs. The 6-12 GB VRAM recommendation is Gemma 4
  E4B QAT so common 6-8 GB cards retain room for context and runtime overhead.
  The recommendation is the starting selection, not the only option, and it
  stops overriding you the moment you pick.
- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
  Ollama chat streams incrementally like the other two providers, instead of
  buffering the full reply before the first token.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune that
  verifies GPU layer candidates before saving a per-GGUF tuned profile.
- Settings, onboarding, and Doctor use the same b-number-aware llama.cpp
  release and platform-asset selector. Semver tags and releases without a
  compatible asset are skipped. Successful installs update the Services path,
  while Doctor can also discover the newest managed b-numbered installation
  when a configured field is stale.
- GPU-aware llama.cpp builds. A runtime-variant setting (Auto by default)
  installs the CUDA build on NVIDIA hardware (with its `cudart` companion
  runtime), the Vulkan build on any other real GPU, and the CPU build when no
  GPU is detected; an explicit choice always wins. A GPU build that fails to
  launch falls back to the CPU build once. `GpuLayers = -1` offloads all
  layers (the default for new servers when a GPU is present), `0` stays on the
  CPU, and `N` offloads exactly N.
- **Mixture-of-Experts CPU offload** (`--n-cpu-moe` / `--cpu-moe`), under
  Services > Advanced engine options. On a MoE model the expert weights are
  most of the file but only a few are active per token, so the useful trade is
  attention on the GPU and experts in RAM. Cutting GPU layers to make a MoE
  model fit gives up the part that actually wants the GPU. Blank or 0 is off
  and changes nothing; a number keeps that many layers' experts on the CPU;
  "all" keeps every expert there. No effect on a dense model. See
  `docs/llama-cpp-features.md`.
- Managed servers launch fast by default for a single user: one request slot
  (`--parallel 1`, so the whole context belongs to one conversation and every
  send reuses the KV cache), explicit prompt caching plus `--cache-reuse`, and
  a pinned embeddings batch pair that silences llama.cpp's clamp warning.
- Updating llama.cpp installs to the resolved install root instead of nesting
  a new version directory inside the previous one, preserves the configured
  runtime variant, and offers to prune superseded version directories (single
  confirm; current and previous kept). Any managed server running on the
  binary being updated is stopped before the update and restarted afterward
  automatically (restart always runs, even if the update itself fails), so
  running servers no longer silently keep the old build until manually
  restarted. When only the backend binary changes (not the CUDA version), an
  already-downloaded matching CUDA runtime is reused instead of re-downloaded.
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
- The Services view's context-fit advisory is hardware-aware: when Hermaeus can
  read the local GGUF's header (layer count, KV head count, head dims) and the
  machine's hardware profile, it estimates the actual weights+KV-cache size at
  the configured context and GPU-layer offload and warns with the arithmetic
  spelled out (e.g. "needs ~13.2 GB (weights ~7.5 GB + KV cache ~5.7 GB); this
  GPU has 8.0 GB"), comparing against VRAM when layers are offloaded or against
  system RAM when running CPU-only. When the header or hardware profile is
  unavailable, it falls back to the old flat "above 16384 tokens" rule so the
  warning never silently disappears. A separate advisory, independent of the
  fit verdict, notes when the configured context exceeds the model's own
  training context. Both are informational only: nothing here edits a value
  or blocks Start.
- Compare Models provides an in-chat practical comparison path for trying the
  same prompt across multiple visible models before choosing one for normal
  conversation.
- Model benchmarks with GGUF discovery, one-click full-suite runs, saved run
  history, deterministic quality checks, rankings that group runs by model
  with a rank, a proportional score bar, and a Details button per run, test
  info modal for case details, reruns, and Markdown/JSON/CSV export. Run
  provenance includes the managed server's KV cache K/V types and Flash
  Attention setting when recorded, so results do not imply those engine
  choices were held constant. Rerun is disabled while a run is already in
  progress, so a second click can no longer interrupt and leak the first run's
  state.
- Benchmark views expose the test-details modal from both the per-result list
  and the best-run ranking rows, and saved benchmark history can be exported
  in bulk as one timestamped folder.
- The Insights tab answers "best across every suite" as well as "best
  overall": each suite gets its own leaderboard ranked on the cases every
  model in it actually ran, and the cross-suite winner is the model with the
  best mean position across those suites, so each suite counts once and a
  large suite cannot outvote a small one. The card names the suites it rests
  on, shows the leader's placing in each (expandable to that suite's full
  board), and names every suite and model it left out. When there is no
  honest answer, it says which case applies instead of naming a winner.
- Doctor checks for untuned GGUF files, stale `llama.cpp` binaries, and pinned
  `Qwen3-Embedding-0.6B` hash drift, with install actions where available.
- Doctor advises when a real GPU is present but inference is still configured
  for the CPU (a CPU-only build is installed, or the chat server offloads zero
  layers), naming the measured consequence and linking the fix in Services.
  Only fires while the chat server is actually responding (a model is loaded
  and would be running at CPU speed right now), not for a stopped server's
  static configuration.
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
- The Services page's Auto Tune also tunes context, not only GPU layers: it
  probes one additional candidate (all layers at the largest context from a
  fixed ladder that fits, capped at the model's training context) before
  falling back to its usual layer-by-layer descent at the originally
  configured context. This can suggest raising context when there is VRAM
  headroom, not only downshifting it when the configured value does not fit -
  the status message reads correctly either way ("found headroom for a larger
  context" vs. "configured does not fit"). The fit estimate honors GGUF
  sliding-window attention metadata when present, so models with cheaper
  interleaved KV layouts are not treated as dense-attention models on every
  layer. A successful context probe is reflected in the editable fields and
  the tune-profile save. This is the only thing in the app that changes a
  context size automatically, and it only runs from this explicitly
  user-clicked action. The compute-buffer/display-overhead headroom used by
  every fit/context calculation in the app is 512 MiB (corrected from an
  earlier 1.5 GiB rough estimate against a real false-warning report).
- Fits-on-your-hardware chips (Fits GPU / Partial offload / Too large) on
  Models-page cards are KV-cache-aware for local files: when the GGUF header
  can be read, the fit reason states the weights/KV split at the model's
  configured or default context (e.g. "~4.1 GB weights + ~1.3 GB KV cache at
  16,384 context vs 8.0 GB VRAM"). The Hugging Face browser's file list and
  the setup wizard's recommended starter model stay on the simpler
  size-only estimator, since no local file exists yet to read a header from.
  All three share a process-lifetime-cached hardware snapshot.
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
  repos and downloads straight into the flat models folder; both the search
  results list and the per-repo file list scroll internally instead of
  clipping when there are more entries than fit. All Hugging Face access is
  anonymous, HTTPS-only, and manual-button-triggered - never on startup or a
  timer - and is disclosed in the Privacy Audit whenever a model is
  repo-linked.
- The local models list excludes companion files that ship alongside a real
  model in the same Hugging Face repo but are not themselves a loadable chat
  model: `mmproj*.gguf` vision-projector files and `mtp-*.gguf`
  multi-token-prediction draft-weight files. Neither has a first-class use in
  Hermaeus today (multimodal and draft-model speculative decoding are both
  out of scope), so they no longer clutter the list as unexplained
  sub-500 MB "models".
- The Services card's server editor exposes first-class llama-server engine
  options next to Context Size/GPU Layers/Threads/Slots: KV cache type (K and
  V independently, f16/bf16/q8_0/q5_1/q5_0/q4_1/q4_0/iq4_nl), Flash Attention
  (auto/on/off), and Context Shift (rolling context for long agent loops), plus
  an "Advanced engine options" section for `--mlock`, `--no-mmap`, and a
  speculative-decoding section (see below). Every default matches the prior
  hand-typed command line exactly (f16 KV cache, auto flash attention, everything else
  off). Auto emits no `--flash-attn` override, leaving llama-server to select
  its supported default; on and off are explicit overrides. Nothing is ever forced, and a value typed into Extra args always wins
  over the equivalent first-class control. A quantized V cache combined with
  flash attention off shows an inline warning (llama.cpp needs flash attention
  for a quantized V cache) but still launches with exactly what was chosen. A
  "Suggest engine settings" button fills Context Size, KV cache type, and
  Flash Attention with a hardware-tier recommendation (from the cached VRAM
  profile and, when available, the model's training context) into the
  editable form only - nothing is saved or applied until Save Config, the same
  contract as Auto Tune. The KV cache type also feeds every context-fit
  calculation in the app (Services card warning, Auto Tune's context ladder),
  so switching from f16 to q8_0/q4_0 visibly raises how much context fits the
  same VRAM.
- The Services card's server editor also exposes an optional "Vision
  projector" picker beside the model row: a `mmproj-*.gguf` file that sits
  alone next to the selected model auto-fills and follows a later model
  switch, otherwise it lists every `mmproj-*.gguf` found there for manual
  choice. Setting one launches the
  server with `--mmproj <path>`, enabling image attachments in chat for that
  server. A model path browsed or previously saved from outside the scanned
  assets root, and the models folder's own casing (`llm` vs `LLM`), are both
  detected against what actually exists on disk rather than assumed.

### Constrained output (r28)

When Hermaeus needs a reply in a particular shape, it stops asking for one and
requires it. A caller attaches a JSON schema (or a GBNF grammar) to a request,
and the provider's sampler enforces it while the tokens are being chosen, so a
malformed reply is not something to recover from, it is something the model
cannot produce.

- **llama.cpp** enforces both forms: a JSON schema through `response_format`
  and a grammar through a top-level `grammar` field.
- **Ollama** enforces a JSON schema through its `format` field.
- **OpenAI-compatible endpoints** vary by server and model, and Hermaeus does
  not guess. Structured outputs are used against `api.openai.com`, where they
  are documented; against any other base URL a constraint is **refused in
  words at the point of use** rather than sent as a field the server may
  silently ignore. That refusal is deliberate: a caller that sets a constraint
  intends to parse the answer without defending against prose, and an
  unconstrained reply that happens to parse looks exactly like success.

  Plenty of compatible servers do support the field and have no way to say so
  (LM Studio, vLLM, llama.cpp behind a proxy), so **Settings > LLM has a
  "This endpoint enforces response_format" checkbox** beside the OpenAI base
  URL. It is off by default, ignored for `api.openai.com`, and it is a
  declaration rather than a probe: you are telling Hermaeus what your own
  server does. If the claim is wrong, the server rejects the request and the
  rejection is surfaced, not retried unconstrained.

Nothing is probed to find this out. A provider reports what it knows about
itself, or the user reports it on the provider's behalf, and a capability
check that loads a model to find out whether a model loads is a
denial-of-service handle wearing a health check's name.

Two callers use it today: **memory auto-summary** (see Memory above) and the
**agent's planner protocol** (`docs/agent.md`). Every existing parse-and-repair
fallback in both places stays, because they are what runs on providers that
cannot constrain. The Chat trace records which shape a turn enforced, or
"unconstrained", so a reply that parsed cleanly can be told apart from one that
was made to.

There is no user-facing grammar or schema editor. The grammar form exists
because llama.cpp's own surface has it, not because anything asks you to write
one.

### Speculative decoding (r27)

A small cheap model proposes the next few tokens and the large model verifies
all of them in one forward pass instead of one pass per token. Where the
proposal was right, those tokens are kept for roughly the cost of a single
decode step; where it diverges, the tail is discarded. **The text produced is
the text the large model would have produced alone**, so this is a speed
optimisation with no quality tradeoff. How much speed depends entirely on how
often the draft guesses right, which depends on the model pair and the content
being generated: it can be a large win, a small one, or slower than not using
it. Run the **Speed Check** (`docs/benchmarks.md`) to find out for your pair.

The Services card's server editor carries one composable section rather than a
checkbox per technique, because llama-server's `--spec-type` takes a
comma-separated list and n-gram speculation and draft-model drafting are not
mutually exclusive:

The selected executable's own `--help` is the gate. Hermaeus discovers the
runtime's advertised speculative types and separate prompt-processing threads,
but enables the n-gram and MTP controls only when that runtime proves them.
Launch repeats the check, so an executable update cannot turn a saved setting
into an ignored flag. Other discovered speculative types stay informational
until Hermaeus has a complete drafter and compatibility workflow for them.

- **N-gram drafting** (`ngram-mod`): costs no additional VRAM, because it
  drafts from the prompt and history themselves rather than from a second
  model.
- **Draft model** (`draft-mtp`): drafts from an MTP (Multi-Token Prediction)
  head. An MTP head is trained as part of its base model and ships inside that
  model's own repository, so it shares the model's vocabulary by construction,
  and at tens of megabytes against a multi-gigabyte target it is the size ratio
  speculative decoding actually wants.
- **The draft model itself** is found the same way the vision projector above
  it is found: a scan for `mtp-*.gguf` beside the selected model and in its
  `MTP/` subfolder. A sole candidate is filled in and follows a later model
  switch; anything you chose explicitly is left alone. Finding the file does not turn
  drafting on. Nothing reaches the launch command until you tick the box, so
  discovery never silently changes how a server runs.

Both boxes write into one underlying `--spec-type` list, because the flag
genuinely is a list and the two techniques compose. Each box only ever adds or
removes its own entry. A manually saved type is never assumed compatible: it
must appear in the selected runtime's help or launch is refused with the type
named.
- **n-max / n-min / p-min / draft ngl**: optional. Blank leaves llama-server's
  own defaults alone.

A server configured with a `draft-*` type is checked before it launches. The
draft path goes through the same traversal and symlink rejection every other
user-supplied path gets; a draft model whose GGUF vocabulary size differs from
the target's **refuses the start**, naming both models and both sizes, because
it cannot verify the target's tokens. A draft larger than half its target
**warns and starts**: that is a bad idea rather than a broken one, and the
Speed Check will show it. Hermaeus Doctor flags a `draft-*` type with a missing
or empty draft path, so a configuration that will fail at start is reported
before you find out by starting.

The combined target-plus-draft weight estimate is shown against available VRAM
before the server starts. This is information, not a block: you may have
reasons, and llama.cpp spills to system memory rather than failing.

Existing configurations upgrade automatically. The old N-gram checkbox becomes
`Types = ["ngram-mod"]` exactly once, which is byte-identical to the flags that
checkbox used to emit.

### Runtime capability changes

When a selected model and managed `llama-server` are probed, Hermaeus compares
the resulting capability snapshot with the previous executable identity. It
records only meaningful capability state changes and speculative types appearing
or disappearing in Activity. Moss gives one short heads-up for that new
snapshot, escalating a disappeared capability that may affect the configured
server. It does not show raw help diffs or send a notification for every start.

### Complete downloads and per-model folders (r27)

Selecting a GGUF in the Hugging Face browser resolves the model's whole **file
set** from the repository tree that was already fetched:

- **Shard siblings** are required. A partial shard set is a model that does not
  load, so shards are part of the download or the download is refused. A sharded
  model is listed once, as its first shard, rather than hidden.
- **A vision projector** (`mmproj-*.gguf`) beside the model, offered and on by
  default. Without it a multimodal model loads and quietly cannot see.
- **An MTP draft head** (`mtp-*.gguf`) beside the model or in an `MTP/`
  subdirectory, offered and on by default. This is the file speculative
  decoding drafts from.

Each file keeps its own SHA256 verification, with deletion on mismatch, and its
own manifest entry. Progress is reported across the set, so a three-shard
download does not appear to finish three times. A partial failure leaves what
succeeded on disk and names what is missing: a 4 GB shard that downloaded
correctly is not deleted because a 60 MB companion failed.

Downloads land in `<models>/llm/<repo folder>/<filename>` rather than one flat
folder. This is not tidiness. Projector discovery is a sibling-directory scan,
so in a flat folder every model was offered every other model's projector, and
companion filenames collide outright: a flat folder can hold exactly one file
named `mmproj-F16.gguf`. Repository subdirectories are flattened into the model
folder, so an `MTP/` head lands where that sibling scan already looks.
**Organize folder** produces the same layout, moving companions with their
model as one group. Files whose repository is known from the manifest go to
that repository's folder; the rest go to a folder named from their own base
name. A file that cannot be attributed is listed as skipped rather than moved
somewhere wrong, and filenames are never changed.

## RAG

- **Watched sources** (r24): a dataset can watch zero or more folders. A
  deterministic, cancellable scan classifies drift against stored source rows
  (hash-preferred, mtime-fallback) without changing anything; **Refresh now**
  on a dataset card shows the plan (N new, N changed, N missing) and applies
  new/changed files through the normal ingest pipeline after confirmation.
  Missing files are never removed by a watched refresh - that stays "Remove
  missing sources"' separate, explicitly-confirmed job. A refresh that would
  drop more than half a dataset's sources warns prominently first (almost
  always an unmounted drive or a bad glob, not an intended purge). Optional
  automatic refresh (off by default, Settings > RAG) can run on app start
  and/or every N hours; it only ever ingests new/changed files, never
  deletes, and its outcome is recorded as an Activity event.
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
- A query (from the RAG panel or a chat send with a Knowledge dataset
  attached) falls back to keyword-only (BM25) search when the embedding
  server is unreachable, instead of failing with a raw exception. The
  planner note and trace name the fallback; a later query with a working
  embedding server is fully semantic again, since the fallback is never
  cached as a sticky failure.
- **Open in chat** on a Dataset Manager card starts a new chat conversation
  with that dataset pre-attached (same "new conversation" path as Ctrl+N, so
  an unsent draft in the current chat is handled exactly as that path
  already handles it).
- **A dataset larger than the in-memory budget is still queried** (r27). It
  used to be dropped by the cache without being cached, after which every
  query read an empty cache entry back, scored nothing, and returned no
  results and no error while re-reading the whole dataset out of SQLite. An
  over-budget dataset is now scanned from storage, and the retrieval result's
  planner notes say so and say it will be slower.
- **Keyword candidates come from a search index**, not from tokenising the
  whole corpus once per query variant. FTS5 generates the candidates and
  `Bm25Scorer` still scores them, so ranking among the chunks that matter does
  not move; the only chunks that stop being scored are ones sharing no query
  term. The index is backfilled lazily on first search of a dataset, so an
  install that never opens the RAG panel never pays for it at startup.
- **The in-memory cache holds embeddings, not documents.** It keeps chunk ids
  and one contiguous embedding block; content, paths and titles stay in SQLite
  and are read for the handful of chunks that survive ranking. The footprint
  per chunk stops varying with document size, so the budget is exact
  arithmetic rather than an estimate over strings.
- **Each dataset's search-index size is shown against that budget** on its
  Dataset Manager card, in the same factual register as the rest of the
  dataset health line, so a corpus outgrowing memory is visible while it
  grows rather than discovered afterwards as a slow query.

## Speech input

Local speech-to-text, off by default. The local backend is in-process ONNX (no
managed subprocess): Whisper base (`onnx-community/whisper-base`, pinned
revision, every file SHA256-verified), the same posture as the native Kokoro TTS
voice - nothing downloads until an explicit install action, and inference then
runs fully offline. Transcripts include punctuation and casing, and the language
is detected rather than assumed. Long recordings are transcribed in fixed
30-second windows, so memory is constant in file length. A remote,
OpenAI-compatible backend is available but never the default, and reuses the
same OpenAI credential Chat and TTS already use rather than asking for it
twice. Configured from **Services > Voice**: provider, input device, model,
and an install action, alongside a **Transcribe audio file...** action that
runs the whole pipeline against a file you pick - the one path that is
verifiable with no live microphone. A shared dictation control (the
microphone icon next to the chat input) records on click, transcribes on a
second click, and inserts the result at the cursor for you to edit and send
yourself - it never sends on your behalf. Doctor gains backend and
microphone checks; Privacy Audit names the destination explicitly whenever
the remote provider is selected, since voice is a higher-sensitivity case
than the image-attachment disclosure it already has. Audio is always
transient: a capture (or an uploaded file for remote transcription) is
processed and its temp copy deleted immediately, never persisted, logged, or
attached to a conversation. See [docs/voice.md](voice.md).

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
  Linux tar link entries are validated to stay inside the archive root and
  materialized as regular companion-library files, so the loader can resolve
  the release's SONAMEs without trusting archive-created filesystem links.
  Model downloads verify a SHA256 hash when Hermaeus has trusted hash metadata
  for that exact URL. Models installed from Hermaeus's pinned Hugging Face
  actions retain their repository provenance in the existing model manifest.
- Local AI setup scans are voice-provider aware. Kokoro setup checks Kokoro
  Python imports and does not show XTTS script or model actions unless XTTS v2
  is selected.
- Generated XTTS helper scripts escape configured model/output paths before
  writing Python source, so unusual quotes or newlines in paths cannot alter the
  script body.
- The Local AI setup can now detect available GPU backends when creating
  a Python venv and will suggest a device (`cuda` for NVIDIA, `rocm` for
  AMD/ROCm, `mps` for Apple Silicon, or `cpu`) to use for TTS/model inference.
  You can still override the selected device in **Services -> Voice** after
  setup.
- First-run Setup Wizard: on first launch Hermaeus runs a guided 6-step
  setup wizard to select the data root, local AI assets root, chat backend,
  model folder, voice provider, and to run the Hermaeus Doctor for a quick
  health check before you start using the app. Brief factual Moss guidance
  explains each step and what can be skipped. The wizard can be skipped or
  re-run from the Settings panel. If you navigate to Logs or another panel
  mid-setup, **Resume setup** returns to the same incomplete step.
- Finishing or skipping the wizard immediately starts configured servers and
  loads chat models, RAG datasets, and agent/benchmark data - no restart or
  extra navigation needed to make first use of the app work.
- Re-running the wizard and choosing a different data root moves your
  existing databases to the new location the same way Settings' data-root
  change does (with a confirmation toast), instead of switching to an empty
  root. A target folder that already has conflicting data files is refused
  with an explanation, and the current data root is left untouched.
  The fixed per-user `hermaeus.lock` is process coordination rather than user
  data, so migration and backup enumeration leave it at the bootstrap root;
  another process still cannot acquire it.

## Hermaeus Doctor

- Hermaeus Doctor checks for storage, runtimes, voice, RAG, GPU, and secrets.
- Doctor distinguishes a configured llama-server path from an executable that
  actually runs and reports a recognizable build. A missing companion library
  is an error rather than Ready. Update identifiers from different schemes are
  reported as not comparable instead of being called current.
- Hermaeus runs Doctor in the background after launch and raises a notification
  when errors or warnings are found, so startup problems are visible before the
  Doctor panel is opened.
- Hermaeus Doctor now validates the configured Python and voice backend
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
  embedding server at the verified Qwen3-Embedding-0.6B file. Existing Nomic
  installations remain selected and untouched until that verified install completes.
  Download progress is rate-limited and coalesced into one bounded updating
  state. Navigating away does not destroy the singleton operation; returning to
  Doctor shows the same progress or completion state.
- Doctor flags a blank embedding endpoint (RAG's `EmbeddingBaseUrl`) while
  memory or RAG is enabled: embedding requests silently fall back to the chat
  server otherwise, queuing behind chat generation on a single-slot
  llama-server.
- Doctor checks the newest published GitHub release against the running app
  version and flags a warning if a newer one exists (the same check runs for
  the `llama.cpp` binary Hermaeus manages). The check only compares version
  numbers; it never downloads or installs anything, and its fix action just
  opens the releases page in your browser so you can update yourself. This
  needs the `MortisDei/hermaeus` GitHub repository to be public to succeed;
  while it is private the check fails closed and reports itself as unable to
  reach GitHub, the same as it will for any other network outage. Both
  update checks cache their result for an hour rather than calling GitHub on
  every scan: GitHub's anonymous API allows only 60 requests/hour per IP,
  and Doctor's automatic startup scan plus manual rescans could otherwise
  exhaust that quota on background checks alone and leave no headroom for
  an actual llama.cpp update click, which failed with a rate-limit error in
  practice before this caching was added.
- Doctor reports whether the previous session exited cleanly, using a small
  local-only lifecycle journal (no telemetry, nothing leaves the machine).
  If Hermaeus did not shut down cleanly last time (a crash or force-close), the
  warning names the last recorded operation, and if an unhandled-exception
  crash log exists from that session (written under `{DataRoot}/logs/`) its
  detail is read back and shown too, so a native-level crash that bypasses
  all managed error handling still leaves a real starting point for
  diagnosis instead of a generic message. Neutral breadcrumbs ("running", a
  completed session load) are not mistaken for the operation that was
  actually in flight when a crash happened.
- Doctor's previous-session title now agrees with its warning detail. Error
  notifications are also recorded through Runtime Logs, and notification
  history formats stored UTC timestamps through the shared local-time display.
- Managed embedding warm-up waits for the matching localhost embedding server
  to reach Running. An intentionally stopped or not-yet-started service no
  longer produces a misleading connection-refused startup warning.

## Activity

A reverse-chronological local record (r24) of what Hermaeus actually did and
whether it worked - managed server start/stop/crash, Doctor scan results,
and RAG ingest/refresh outcomes - each row an explicit outcome (Succeeded,
Partial, Failed, Cancelled, Running), never collapsed to a boolean. Answers
the "did that actually work" question the palette and per-panel status text
alone cannot: a plain list, not a new dashboard. "Clear activity history"
(Settings-style confirm dialog, plain-language copy) removes trace rows
only. Shares the existing trace store rather than a parallel one, so
per-kind pruning and exclusion from usage-count rollups fall out of the
existing mechanisms; entries are redacted before being persisted.

Coverage is now the full set the recorder was written for: managed servers,
model downloads (a partially downloaded file set records as Partial, with the
reason naming what is missing), model updates including a refused hash or a
failed swap, RAG ingest and watched-source refresh, Doctor
scans, backup and restore in both directions, memory auto-archive sweeps
including the ones that archive nothing, and the managed voice backend's
start, stop and failure. A recorder failure never fails the operation it is
describing.

A row that names a specific artifact is activatable and opens it, routing
through the same navigation the command palette uses so there is one answer
to where a task or a dataset lives. Agent rows open their task, RAG rows
their dataset, chat rows their conversation, memory rows their memory. A row
with nothing specific to open (a Doctor scan, a server start) stays inert and
looks inert rather than offering a link that goes nowhere.

Consecutive rows within a minute of each other sit under one time heading.
That is arithmetic on the clock: it says these things happened together, not
that they are related, and there is deliberately no model-written summary of
a group, no "likely cause" field, and no correlation claim.

## System Integration

- App shutdown disposes the service container asynchronously so an
  async-only background service (like an active MCP session) no longer
  raises an unhandled exception on window close; a hung MCP child process is
  abandoned to the existing job-object cleanup after a bounded 5-second wait
  rather than blocking exit indefinitely.
- System overview for app version, CPU, RAM, storage, databases, managed
  components, and GPU/VRAM visibility (GPU/Components shown before the
  Privacy Audit, since hardware is what most people check first). On
  Windows this now reports real
  available/total RAM (not the GC's view), an honest OS name and build
  (Windows 11 detected by build number, not the kernel string's misleading
  "10.0.x"), the marketing CPU name from the registry, and GPU name/VRAM
  from a registry fallback when `nvidia-smi` is not on PATH.
- Privacy Audit dashboard connects local-first posture into one view covering
  configured remote providers, local providers, network-facing managed server
  flags, secret backend health, runtime log redaction, data-root backup status,
  and features that may send data remotely, including images attached to a
  chat message when a remote chat provider is selected.
- Runtime logs with filters, copy, and redacted diagnostics export.
- Runtime log entries are redacted before disk persistence and archive
  rotation avoids overwriting archives created in the same second. Redaction
  covers common API keys, bearer tokens, GitHub-style tokens, AWS-style access
  keys, Azure-style key assignments, password parameters, query-string secrets,
  and home paths.
- Tray integration, minimize-to-tray, local hotkeys, and Windows system-wide
  hotkeys. Local hotkeys: Ctrl+Space toggles quick chat, Ctrl+N opens a new
  conversation, Ctrl+Shift+S opens Services, Escape closes quick chat. Quit
  is available via the tray menu and window close, not a hotkey - an
  instant, unconfirmed Ctrl+Q quit was removed.
- Deleting a conversation asks for confirmation first, matching every other
  destructive action of similar weight (dataset delete, reindex, benchmark
  history clear, backup restore).
- Single-instance guard: launching Hermaeus while another instance for the
  same user account is already running exits immediately instead of opening
  a second window, since two processes would otherwise write to the same
  SQLite data root with no coordination between them.
- Toast notifications throughout the app with opaque popup backgrounds.
- The Services nav icon's "any server running" indicator updates live on
  every server start/stop/crash, not just after a settings save.
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
  When both `Models` and `models` exist, Hermaeus prefers the folder containing
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

Hermaeus connects its local-first systems around a workspace root so chat, RAG,
agent state, project instructions, and local safety checks can share context.

### Projects

A Project (r24) is a named container - folder root, default chat model,
default RAG dataset, default system prompt, and a brand-palette color - that
Chat, Agent, and RAG all read from and write into. Switching the active
project (Ctrl+K or the switcher) sets that context everywhere at once: a new
conversation inherits the project's default model/prompt/dataset, the Agent
panel pre-fills (never auto-selects) the project's folder root as the
workspace, and RAG's default dataset follows. Conversations, RAG datasets,
and agent tasks each carry an optional project id so they can be scoped and
filtered later. A project can be created empty, from an existing
conversation, or by adopting an already-selected Agent workspace (including
its accumulated workspace-memory notes). See [docs/projects.md](projects.md)
for the full model and switcher/editor behaviour.

This absorbs and supersedes the per-workspace "root folder / preferred
model / linked dataset" sketch this section used to describe as an Agent-only
concept; that data now lives on the Project itself, shared across every
subsystem rather than pinned to the Agent panel alone.

The Agent panel's own **Explain Workspace** scan (below) still produces a
separate, per-folder repo analysis (languages, frameworks, risks) saved to
workspace memory - a Project's folder root is what that scan runs against,
not a replacement for it.

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
Memory is enabled (Settings > Memory), and retrieves from an attached RAG
dataset (see Knowledge, above) when one is attached to the conversation.
Memories actually used that turn collapse behind a single "Memories used: N"
pill that opens a flyout listing the individual memories (each with its
content as a tooltip); clicking one jumps straight to it in the Memories
view. RAG chunks retrieved for the turn render individually as visible,
clickable citation pills under the assistant's reply instead. This closes a
gap from the original memory and RAG features: both existed as services but,
before r21, nothing in chat ever called either one for RAG, and memory
injection was the only half actually wired up.

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
tracked in [docs/review/archived/r2/03-next-level-roadmap.md](review/archived/r2/03-next-level-roadmap.md),
not documented here as existing behaviour.

### Local API

An optional, off-by-default loopback HTTP host (`Hermaeus.LocalApi`) that lets
other local processes (editor extensions, scripts) reuse Hermaeus's chat,
memory, and RAG query surface without the desktop UI. Enabled and configured
from Settings > Local API: a checkbox, a port (127.0.0.1 only), and any
number of named per-app bearer tokens (add one, name it, copy the generated
value; revoke any one individually without affecting the others). The host
refuses every request with a 503 until at least one token exists. Settings
shows a live host status label (Running/Stopped/etc.) next to the checkbox.
Every call is logged to the shared trace store keyed by the verified token
name that authenticated it (the caller-supplied `X-Hermaeus-Client` header is
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
- `GET /v1/capabilities` - what this instance can currently serve: the routes
  it exposes, the app version, and per feature (chat, RAG, memory,
  embeddings) whether it is usable right now with one sentence saying why not
  when it is not. It reports rather than probes: no model load, no server
  start, no network call, no embedding pass. It names no paths, keys, tokens
  or dataset names, only counts.

## Mascot and branding

Hermaeus's mascot is Moss, Keeper of Knowledge (not the AI itself); full
identity, personality, and visual spec live in [docs/mascot.md](mascot.md). A
flat-vector icon-scale rendering (`Controls/MossIcon`) is a placeholder built
to the icon spec, pending real illustration. It now appears in every empty
state that has nothing to show yet (Chat, Agent, Benchmark, Memories, RAG),
in a small greeting on the first-run setup wizard, and in its two original
spots (the Services error banner and the RAG ingest-progress line) - never in
the chat transcript itself.

The app icon, taskbar icon, and system tray icon use the "Archivist's Seal" mark
(a gold H monogram grown through with a tree and book) - see `docs/mascot.md` for
the icon source and cropping notes.
The UI theme uses the brand colour palette (Forest green accent, Copper/Amber
highlights) - see `docs/mascot.md` for the full palette. Typography uses the
OS-native UI font by default (r21: the three embedded brand typefaces were
removed for readability); Settings > Interface > Typography lets the user
pick their own font for headings, body text, and code independently. Settings
> Interface > Theme (System/Dark/Light) is applied immediately on change and
on startup. Tooltips use an explicit branded, theme-aware style, and
secondary/tertiary text uses two first-party opacity classes (`hint`,
`faint`) with a contrast floor, instead of ad-hoc per-control opacity values.

Rows of icon buttons keep one continuous hand cursor. The gaps between such
buttons (the notches where two rounded corners meet, plus any spacing the
container adds) belong to no button, and a container with no background is not a
hit-test result either, so the pointer used to fall through to the window root
and the cursor flickered between the hand and the arrow when crossing a row.
Containers holding only buttons are handled automatically
(`Hermaeus.Desktop/Controls/IconBarCursor.cs`); containers that also hold text
set it in xaml, so the hand never spreads across a panel of labels or inputs.

Tooltips are drawn by the app rather than by Avalonia's built-in tooltip
service (`Hermaeus.Desktop/Controls/OverlayToolTip.cs`). Avalonia shows them in
a popup whose outer edge produces no hit-test result, which makes the service
close and immediately reopen the tooltip and flickers the mouse cursor on every
button in the app (upstream AvaloniaUI/Avalonia#19218). The app's version
renders into the window's overlay layer with hit-testing off, so it cannot
affect what the pointer is over, and is clamped to the window so a control near
the screen edge behaves like any other. Views are unaffected: they still set
`ToolTip.Tip` as before.
