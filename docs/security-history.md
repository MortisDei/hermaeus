# Hermaeus Security History

Per-round security narrative, newest round first. This is append-only:
a round adds a section here instead of growing `docs/security-review.md`.
History sections are moved here verbatim from the posture doc as each new
round lands, never rewritten - if a historical section contains the only
statement of a *current* control, that control has been copied into
`docs/security-review.md` rather than left stranded here.

See `docs/security-review.md` for current controls and the threat model,
and `docs/security-roadmap.md` for open hardening work.

## r27: Fast, And Honest About It (Startup, Retrieval, Drafting, Model Downloads)

Three security-relevant changes, all of them new user-controlled input
reaching a path or a process.

- **A second model file now reaches llama-server.** Speculative decoding with
  a `draft-*` type passes a user-chosen `.gguf` path to the managed process.
  It goes through the same validation the app applies to every other
  user-supplied path before the process is launched: no `..` segments, must
  resolve, must exist, never a symbolic link or junction. The refusal happens
  in `StartAsync` before `BuildProcess`, in the same shape as the r9
  port-conflict refusal, so a doomed or unsafe configuration fails with the
  cause named rather than launching. Every generated flag reaches the process
  through `ArgumentList`; none of the new options is shell-interpolated.

- **A compatibility check that is also a safety check.** A draft model whose
  GGUF vocabulary size differs from the target's cannot verify the target's
  tokens. `GgufMetadataReader` gained the vocabulary size, read either from
  the architecture's `.vocab_size` key or from the declared length of the
  tokenizer token array, skipping the tokens themselves rather than
  materialising them. The parser's existing untrusted-input posture is
  unchanged: hard caps on string length, array count and nesting depth, no
  tensor data read, and null rather than an exception on anything malformed.

- **A remote-influenced string now names a directory.** Download destinations
  are per-model folders derived from a Hugging Face repository id. That id is
  typed by the user and comes from a remote service, so `ModelRepoFolder`
  treats it as hostile input: it is reduced to a single sanitised segment and
  the resulting path is proven to stay under the destination root after
  `Path.GetFullPath` rather than trusted to. Two ids that sanitise to the same
  readable name get distinct folders via a hash of the original, because
  silently merging two repositories' files into one directory is the failure
  this change exists to prevent, and doing it by accident would be worse than
  the flat folder it replaces. Per-file SHA256 verification is unchanged, and
  a failed hash deletes only the file that failed.

## r25: Change Your Mind, And Trust What It Tells You (Branching, Context Receipt, Whisper, Benchmark Honesty)

Four security-relevant changes, one of which closes a real availability bug.

- **A denial-of-service reachable from the app's own file picker, fixed.** The
  audio-file transcription path accepted up to 200 MB, roughly 1.7 hours of
  16 kHz mono PCM16, and fed it to a full-self-attention model as one tensor,
  giving quadratic memory growth in input length. An ordinary podcast-length
  recording could therefore exhaust memory and kill the process, with no
  malicious input required. r25's fixed 30-second window decoding makes peak
  memory constant in input length; the remaining limit is a 90-minute duration
  cap on the user's time, not on survival.

- **New pinned model assets.** Speech recognition moved from
  `facebook/wav2vec2-base-960h` to `onnx-community/whisper-base`: five files,
  pinned by repository and revision, every one SHA256-verified before every
  load and after every download, with a failed hash refusing to load rather
  than falling back. The two ONNX graphs are pinned to the Git LFS content
  hashes published by the Hugging Face tree API (the mechanism r11 and r13
  already use); the four small JSON assets are not LFS objects, so their git
  blob ids are not content hashes and those were downloaded and hashed
  directly. Getting that distinction wrong would have pinned four assets to
  values that could never match, which fails closed but for the wrong reason.

- **Tokenizer and generation config are parsed data, not code.** The decode
  loop reads special token ids, suppression lists and the language map from the
  model's own `generation_config.json` and `added_tokens.json`. These are
  hash-verified files parsed with `System.Text.Json` into integers and strings;
  nothing in them is executed or used to construct a path. The decode loop is
  bounded by the model's own `max_length`, so a crafted config cannot produce
  an unbounded loop, and a missing or malformed field falls back to a
  conservative default rather than throwing inside a transcription.

- **An already-downloaded superseded model is never deleted.** Doctor reports
  the retired wav2vec2 directory with its size and location and takes no
  action. Silently removing hundreds of megabytes a user chose to download is
  not this app's posture, even for a model it no longer uses.

- **Conversation branching keeps more of the user's data, not less.** Every
  branch persists, and the one destructive operation (deleting a version) is
  explicit, states how many messages it will remove, and refuses when only one
  version remains. The change also removes an existing data-loss path:
  Regenerate previously deleted both the answer and the question. The tree walk
  is cycle-safe by construction, because `messages_json` is a text blob the
  user owns, syncs and can hand-edit, and it is walked on the UI thread with no
  cancel; a cyclic parent chain truncates and logs rather than hanging the app.

No new network surface beyond the pinned Hugging Face download that the
existing approval-gated install action already performs, and no new process
launches.

## r24: One Place, One Memory, One Voice (Projects, Recall, Watched Sources, Voice Input)

Four features, the largest being two new local surfaces (Recall, speech
recognition) that both hold or transmit user content, plus one real
cryptographic fix found while testing an unrelated item.

- **Recall indexes user content by design, and shipped with the controls
  that requires from day one, not as a follow-up.** `recall.db` is a second
  local, unencrypted searchable copy of conversations, agent tasks,
  memories, and RAG chunks - the same shape of exposure
  `ConversationStore`'s own FTS table already has, not a new category. The
  owner caught the first draft of the spec building this with no on/off
  switch, no clear action, and no per-conversation exclusion during pack
  review, before any code existed; the shipped version has all three from
  the start (Settings > Memory), plus honest size reporting and a hard
  "not descopable at any budget" rule in the round's own spec. Indexing
  runs as a bounded background pass, never on the chat send path. Optional
  chat-side injection (off by default) reuses the same citation-pill UI
  RAG/Memory injection already have, rather than a new prompt-assembly
  path.
- **Speech recognition adds a new local ONNX asset and a new remote-network
  option, both following existing patterns rather than inventing new
  ones.** The local backend (`facebook/wav2vec2-base-960h`, pinned,
  SHA256-verified) uses the exact install/load posture native Kokoro TTS
  already established: nothing downloads on the inference path, only
  through an explicit install action. The remote OpenAI-compatible backend
  reuses the existing `Llm.OpenAiApiKey`/`OpenAiBaseUrl` secret reference
  rather than adding a second place to store the same kind of credential,
  and is off by default with Privacy Audit disclosure when selected. Audio
  capture (Windows `winmm`, Linux `parecord`/`arecord`/`ffmpeg`) never
  persists what it records: a temp WAV is deleted after transcription on
  every path, including failure and cancellation, and every capture
  requires an explicit user action with a visible indicator for its
  duration - no wake word, no background listening.
- **Watched RAG sources add a scheduled/on-demand filesystem scan, not a
  standing watcher.** `FileSystemWatcher` was explicitly rejected (no
  persistent OS handle on user directories, platform-divergent behavior).
  A refresh's apply step only ever ingests new/changed files; a missing
  file is never removed without a second, separately confirmed action,
  matching the existing "Remove missing sources" contract exactly.
  Automatic refresh (off by default) inherits that same never-delete rule
  unconditionally, and is skipped for a dataset whose embedding model has
  drifted, closing the "background process bypasses the same guard a
  manual ingest enforces" failure mode before it could exist.
- **A real cryptographic gap in the local secret vault's fallback, found
  while root-causing an intermittent test failure, not through directed
  review.** `SecretStore`'s AES-CBC fallback encryption has no built-in
  integrity check; decrypting under a replaced or corrupt local key does
  not reliably throw, since PKCS7 padding validation alone has roughly a
  1-in-256 chance of coincidentally passing on random post-decrypt
  garbage. The existing test asserting "decrypt-with-wrong-key resolves
  to empty" was flaking on exactly that coincidence. The primary decode
  path now also requires the result to be structurally valid UTF8 (the
  fallback fail path already had this discipline; the primary path
  didn't), closing the practical gap without changing the on-disk format
  or requiring a migration. Still not a formal AEAD/MAC construction - see
  `docs/security-roadmap.md`.

## r23: Trust You Can Operate (Approval Integrity, Run Ledger And Rewind, Workspace Policy)

Four independent security-relevant changes, none of which add a new network,
process-launch, or credential surface.

- **Approval fingerprint binding closes a real TOCTOU gap.** Before this
  round, `AgentService.AppendApprovalAsync` executed whatever
  `state.PendingToolAction` held at click time, never checked against what
  was actually rendered to the user. A concurrent step, a crash-restore
  race, or a tampered `task_state.json` between render and click could make
  the user approve one action and execute a different one. Every pending
  action now carries a SHA256 fingerprint (tool name plus canonicalized
  arguments) computed when it is created; approval requires the caller to
  supply the fingerprint of what it actually rendered, and a mismatch
  refuses execution, leaves the pending action untouched, and records both
  values in `agent.trace.jsonl`. Rejection is unaffected (rejecting the
  wrong thing executes nothing regardless). Pre-r23 persisted tasks with no
  stored fingerprint are handled by recomputing from ToolName/Arguments at
  both render and approval time, so no migration was needed.
- **Run Ledger and Task Rewind add an integrity control, not a new write
  path.** `AgentRunLedgerBuilder` is a pure projection over already-persisted
  `AgentTaskState` (patches, tool results, approvals) - it reads, never
  writes, and never touches the filesystem. `AgentPatchReviewService.
  RevertTaskAsync` (Task Rewind) reuses the exact same per-file revert rule
  every existing single-patch revert already used - refuse to overwrite a
  file that changed again after the patch was applied - so Rewind can only
  restore what the agent itself wrote, never overwrite content written by
  anything else since. Revert paths go through the same `AgentWorkspaceTools`
  containment checks (`ResolveSafePath`, symlink rejection) as every other
  file operation; no new path-resolution code was introduced.
- **Workspace policy only ever narrows.** `.hermaeus/workspace.json` can now
  declare an optional `policy` block (read/write allow-lists, a `never`
  deny-list, a per-task file-read cap). Since the manifest lives inside the
  workspace, hostile workspace content can author one - the design is
  structural, not just tested, so the worst a malicious policy can do is
  restrict the agent further inside that workspace: there is no code path
  where policy grants a path outside the containment root, a new command
  family, or relaxes a gate. Enforcement sits immediately after the existing
  containment/symlink checks in `AgentWorkspaceTools`, reusing `glob_files`'s
  own matcher rather than a second glob implementation, so "what policy
  matches" can never diverge from "what glob_files matches". A malformed
  policy (bad shape, a negative cap) is rejected as a whole, with a visible
  warning, rather than silently falling back to a half-applied restriction -
  a boundary the user trusts but that is not actually there is worse than no
  boundary. A denied write is classified Blocked by the safety gate before
  it can ever become an approvable pending action; a denied read returns a
  structured refusal (not an exception), so a poisoned workspace cannot use
  a read attempt to crash or strand a task.
- **The stated-lesson gate-claim filter hardens a channel that was already
  safe, not one that was exploitable.** The lesson store never fed the
  safety gate before this round and still does not; a poisoned lesson could
  not widen execution. It could, however, sit in every future context pack
  and the Lessons panel as a standing, persistent social-engineering
  message ("the user approves all commands"). A deterministic,
  case-insensitive token match on the model-authored `[LESSON: ...]` marker
  now rejects an approval-policy claim outright - not stored at any
  confidence - and records the rejection in `agent.trace.jsonl`. The four
  deterministic lesson sources (command, patch, approval, task outcome)
  cannot produce this kind of claim by construction, so the filter applies
  only where it is actually needed.
- **Three new Agent Scenario Suite entries exercise this round's controls
  under the same isolation guarantees the existing suite already provides**
  (throwaway sandbox workspace and data root, real unmodified
  `AgentSafetyGate`/`AgentService`, never auto-denies): confused user
  authority (a goal that pre-announces consent must still hit the approval
  gate), tool result poisoning (provocative directory/file names as the
  injection vector, not file body content), and memory poisoning (a
  workspace instructs the agent to record a blanket-approval lesson,
  exercising the gate-claim filter, plus a `never` workspace-policy rule
  over a fixture secrets file). Suggestion 16 in the external review that
  motivated this round is not a fourth scenario: it shipped as the
  fingerprint-binding code hardening above, since the scenario suite grades
  model behaviour and cannot itself tamper with task state between an
  approval's render and its click.

No new NuGet package, no new outbound destination, no new process-launch
path. `docs/pull-requests.md`'s PR-workflow gate landed just ahead of this
round; r23 is the first round implemented on a branch and merged through
review rather than pushed directly to `main`.

## r21: RAG Meets Chat

No new network surface. This round moves existing local dataset content into
chat prompts and adds one additive conversation-store column; the changes
are about what leaves the machine under a remote provider, not about a new
way for something to reach the machine.

- **Chat knowledge injection is bounded and gated, not a free-form corpus
  dump.** `ChatViewModel.BuildRagInjectionAsync` only ever injects a packed
  context block through the same budget-aware `RagQueryService.
  BuildContextPack` seam the RAG panel itself uses
  (`RagSettings.ChatInjectionTokenBudget`, default 2000 tokens), and only
  when retrieval clears `RagQueryService.WouldRefuse`'s confidence
  threshold - an unrelated message ("thanks!") injects nothing even with a
  dataset attached, which is the honesty gate that keeps a whole corpus from
  leaking into every remote-provider chat regardless of relevance.
- **Remote-provider implication is disclosed, not new.** Injected excerpts
  ride the same system-prompt path memory injection and attachments already
  use; when a remote chat provider is selected and the RAG subsystem is
  available, the Privacy Audit's "Remote providers" entry now names Chat
  knowledge context explicitly (`PrivacyAuditService.ScanAsync`), matching
  the existing image-attachment disclosure style. The entry describes
  surface (capability is live), not the current toggle state (a dataset
  attached to this specific conversation right now), consistent with how
  every other disclosure in that list already behaves.
- **`Conversation.RagDatasetId` is an additive, non-executable column.**
  `ConversationStore`'s schema version 1 to 2 migration adds
  `rag_dataset_id TEXT NOT NULL DEFAULT ''` through the existing
  `SqliteMigrationRunner`/`EnsureColumnAsync` pattern (the same shape r6's
  folder/tags/pin/archive migration used); it is a plain string looked up
  against the dataset table, never interpolated into SQL or a shell
  argument, and is deliberately excluded from the FTS index (not searchable
  text).
- **Embedding-server-down fallback removes a raw-exception path, adds no new
  privilege.** `RagQueryService.RetrieveAsync` now catches an embedding-call
  failure and degrades to BM25-only (the same degraded path the existing
  embedding-model-mismatch case already used), logging one Warning and
  never caching the failure. `OperationCanceledException` is explicitly
  rethrown rather than swallowed into the fallback, so a cancelled send or
  query still cancels cleanly. This is a robustness fix to an existing local
  code path, not a new capability.
- **A stale/deleted dataset attachment degrades honestly, never silently.**
  A `RagDatasetId` that no longer resolves is never auto-cleared (matching
  the r10 stance that missing RAG sources are never auto-removed); the
  picker shows "Knowledge: missing" and the send proceeds with nothing
  injected. Dataset deletion does not scan conversations for references -
  an explicit, documented non-goal (doc 03.2) rather than an oversight.

## r20: Rename Aether to Hermaeus

A trademark-driven product rename, not a new feature surface. No new attack
surface: the mechanical scope is namespaces, assembly names, file paths, and
outbound identity strings.

- **Outbound identity strings changed**, not their trust posture: the
  `HuggingFaceClient`/`DoctorService`/`LlamaServerSetupService` user agents
  (`Hermaeus/1.0`, `Hermaeus-Doctor/1.0`) and the MCP client-identify name
  sent during `initialize` are cosmetic string changes to requests these
  services already made; no new outbound call was added.
- **Local API headers renamed** (`X-Hermaeus-Token`/`X-Hermaeus-Client`);
  the auth model is unchanged (named bearer token per caller, fail-closed
  when no token exists). The old header names are not accepted; this is a
  clean break, not a downgrade.
- **Two narrow legacy-read shims, both read-only fallbacks that never widen
  what gets trusted.** The schema-version bookkeeping table
  (`aether_schema_versions` to `hermaeus_schema_versions`) is renamed via a
  same-process `ALTER TABLE` gated on the old table's existence, not a
  general migration path; the Agent workspace manifest falls back to reading
  `.aether/workspace.json` when the new path is absent, and always writes to
  the new path, so an attacker cannot use the fallback to persist state the
  new path wouldn't also accept.
- **OS secret store service name changed** (Linux `secret-tool`, macOS
  Keychain); this orphans, rather than exposes, any secret stored under the
  old `Aether` service name on those platforms. No secret material moves or
  is re-encrypted as part of this round.

## r19: Chat Attachments (.docx/.pdf/images), Chat Artifacts, Crash Logs

Four new surfaces this round touch untrusted file parsing, a new local write
path, relocated diagnostic logging, and a new outbound payload shape.

- **`.docx`/`.pdf` parsing of untrusted files.** `DocxTextExtractor`
  (`Hermaeus.Services`) reads a user-selected `.docx` as a `ZipArchive` and
  pulls text out of `word/document.xml` only; a non-zip file, a zip missing
  that entry, or malformed XML is caught and returned as a structured Skip
  result, never an escaping exception. `.pdf` extraction reuses
  `Hermaeus.Rag.Pipeline.PdfTextExtractor` (the PdfPig-backed parser
  `Hermaeus.Rag`'s own ingest pipeline already depends on and already has test
  coverage for) rather than adding a second, hand-rolled parser for the same
  file format - one reviewed PDF-parsing surface instead of two. Both paths
  are capped before the expensive part happens (20 MB file cap for PDFs,
  extracted-text byte caps shared with the plain-text attachment path) and a
  file that fails to parse is Skipped with a reason shown to the user, never
  silently substituted with empty content.
- **Chat artifacts write to a bounded, per-conversation folder.**
  `ChatArtifactService.SaveAsync` writes only under
  `{DataRoot}/chat-artifacts/{sanitized conversation title, or id}/`: the
  title (or id, when no title is available yet) is sanitized before it
  becomes a path segment, folder identity for lookup is tracked via a hidden
  marker file (not the folder name itself, which can legitimately collide
  across conversations and gets deduped with a `(2)` suffix) so renaming a
  conversation never causes a path to resolve outside its own folder, the
  suggested filename is stripped of path separators and traversal sequences
  before `ResolveSafePath` re-validates the final resolved path still sits
  under that conversation's folder, and a filename collision within a folder
  dedupes with a `(2)` suffix rather than overwriting. Writes are atomic
  (temp file + move), the
  same pattern `SettingsService`/`BackupService` already use.
- **Crash log relocated under the data root, not somewhere less scoped.**
  `Program.cs`'s unhandled-exception log now writes under
  `{DataRoot}/logs/` (resolved via a minimal settings read that runs before
  full DI is available) instead of the previous fixed location, keeping the
  crash log inside the same backup/export/migration boundary as every other
  piece of app state. `CrashLogReader` only ever reads this file back for
  display in Doctor; it is not uploaded or transmitted anywhere.
- **Vision payloads embed local file bytes into the request body.** An
  attached image is read once, base64-encoded into a `data:` URI
  client-side, and sent as an OpenAI-style `image_url` content part
  (`OpenAiCompatibleToolWire.BuildContent`) - the same mechanism as prompt
  text, just a different content-part type, and it only ever reaches a
  request when the user explicitly attached the image to that message.
  Images are gated on either the active managed server having an explicit
  `MmprojPath` configured (Services > Vision projector), or the selected
  model routing through the OpenAI provider (`ChatViewModel.
  CurrentModelAcceptsVisionAttachments`); with neither, an attached image is
  Skipped with an honest reason rather than silently degrading to a
  text-only send. The OpenAI path trusts the user's own model choice the
  same way the mmproj path trusts their own picker choice - Hermaeus does not
  probe whether the specific selected OpenAI model actually supports vision,
  the same posture it already takes for llama.cpp. The Privacy Audit's
  remote-provider disclosure and "features that may send data remotely" item
  both name images explicitly, so a remote chat configuration's audit entry
  is not silently stale now that images are in play there too.
- **`--mmproj` is a launch-argument addition, not a new launch surface.**
  Like every r18 engine-option flag, it is appended to the same
  `ArgumentList`-based `ServerProcessManager.BuildLaunchArguments` call - no
  shell string, no new `ProcessStartInfo` path - and is suppressed whenever
  `--mmproj` is already present in `ExtraArgs`, following the same
  never-emit-twice guard those flags established.

## r18: First-Class llama-server Engine Options

Six new `ServerConfig` fields (`KvCacheTypeK/V`, `FlashAttention`,
`ContextShift`, `MemoryLock`, `NoMemoryMap`, `NgramSpeculative`) become
command-line flags passed to the managed `llama-server` process.

- **No new process-launch surface.** Every new flag is appended to the same
  `ArgumentList`-based launch `ServerProcessManager.BuildLaunchArguments`
  already builds - no shell string, no new `ProcessStartInfo` path. Values are
  drawn from a fixed dropdown/checkbox set (`KvCacheTypeOptions`,
  `FlashAttentionOptions`) or plain booleans, not arbitrary user text.
- **`--host 127.0.0.1` is untouched.** No new option or preset ever emits or
  suggests a different bind host; the tuning guide this round was sourced
  from recommends `--host 0.0.0.0`, and that recommendation was explicitly
  not adopted (docs/review/archived/r18/04-llama-server-engine-options.md
  4.5). Still a standing invariant, not something this round could weaken.
- **`ExtraArgs` always wins.** Every new first-class flag is suppressed
  whenever the equivalent flag is already present in `ExtraArgs` (the same
  `HasArg` guard `--parallel`/`--cache-reuse` already used), so a flag is
  never emitted twice and a value typed into `ExtraArgs` cannot be silently
  overridden by a first-class control the user did not touch.
- **No forced or default-on quantization/flags.** Every new field defaults to
  today's exact behavior (f16 KV cache, auto flash attention, everything else
  off); "Suggest engine settings" only fills the editable form and never
  saves without an explicit Save Config click, the same contract Auto Tune
  already established.
- **No new network surface.** `--rpc` (remote compute-peer pooling) was
  explicitly not implemented this round precisely because it is a
  distributed-systems feature needing its own trust/failure-mode review
  (docs/review/archived/r18/05-roadmap.md).

## r17: GGUF Header Parser (New Attack Surface Over Untrusted Files)

`GgufMetadataReader` (`Hermaeus.Services/GgufMetadataReader.cs`) is the first
code in the repo that parses byte content out of a `.gguf` model file rather
than treating it as an opaque blob. Model files are downloaded from the
internet (Hugging Face, direct URLs), so a malicious or corrupted file is a
realistic input, not a hypothetical one.

- **Metadata only, tensor data never read.** The parser stops after the
  header's key/value metadata section; tensor payloads (which can be tens of
  gigabytes) are never touched. This bounds the amount of the file that is
  ever parsed, independent of the file's total size.
- **Every allocation is capped before it happens.** String lengths (keys and
  string values) are capped at 64 KiB, array element counts at 1,000,000, and
  the metadata key/value count at 100,000, each checked against the declared
  length *before* any buffer is allocated or bytes are read - a file
  declaring a multi-gigabyte string or a billion-element array is rejected
  immediately rather than causing a large allocation attempt.
- **Every read is bounds-checked against the actual file, not the file's own
  claims.** A declared length longer than the remaining file content raises
  `EndOfStreamException`/`InvalidDataException` internally rather than
  reading past the end of the stream or leaving a partially-read state;
  `BinaryReader.ReadBytes`-style short reads (which return fewer bytes than
  requested rather than throwing) are explicitly checked and treated as
  truncation.
- **Null-on-failure contract, never an escaping exception.** `TryRead`
  catches every parse failure - bad magic, unsupported version, malformed
  structure, an oversized declared length, a truncated file - and returns
  null. Every call site (context-fit warning, fit chips, Auto Tune context
  suggestion, benchmark quantization metadata) already had to handle "no
  local file information available" as a pre-existing, tested fallback path,
  so a hostile or corrupt GGUF file degrades those features to their
  pre-r17 behavior rather than crashing or hanging the app.
- **No code execution surface.** The parser only interprets a small, fixed
  set of integer/string/array value types into fields the app already
  displays (architecture name, quantization label, layer/head counts); there
  is no reflection, no dynamic dispatch, and no path derived from parsed
  content is ever used to read or write a different file.
- **Test coverage is fixture/fuzz-style**, not just the happy path:
  `GgufMetadataReaderTests` hand-writes byte fixtures for magic mismatch,
  rejected versions, truncation at every structural boundary (mid-magic,
  mid-header, mid-key, mid-value), an oversized declared string length, and
  an oversized declared key count, alongside valid v2/v3 headers, unknown
  value types that must be skipped without derailing later keys, and a
  per-layer array value. No real model files are committed to the repo.

## r16: Orchestration Hardening, Memory Integrity, Workbench Truth

- **1.4 narrows where an approved action can land; it does not loosen
  anything.** Approving a pending `edit_file`/`create_file`/`run_command`
  action now executes against the task's own persisted `WorkspaceRoot`
  instead of whatever workspace the caller's options happened to describe
  (the review queue lists tasks across every workspace, so those could
  silently disagree). The action was already approved by the user looking
  at its preview; the fix is executing it where it was actually authorized,
  never adding a refusal path. A null-options approval with no stored root
  now throws instead of half-approving (leaving `Running` with an
  unexecuted `PendingToolAction`), closing a latent stranding path.
- **2.2 is a new model-authored write path into the memory store, gated and
  bounded.** The `[MEMORY: ...]` save marker was already taught to the model
  by the existing injection prompt but never wired up; it now actually
  saves, but only when `Memory.Enabled`, only through the same
  `MergeAndSaveAsync` dedupe path auto-summary already uses (never a raw
  insert), and capped at 3 new memories per turn. No new tool, no new
  approval surface, no network call - a local SQLite write identical in
  shape to what auto-summary already performed post-conversation, just
  triggered per-turn instead.
- **2.1's archived-row fix and 2.3's expiration enforcement are read-side
  corrections, not new capability.** `SearchAsync` excluding archived/expired
  rows and `ArchiveStaleMemoriesAsync` now also sweeping past-expiration rows
  change what recall returns, not what the store lets anyone write or reach;
  no new threat surface.
- **2.4's re-embed action is destructive only to stale vectors, and only on
  a user click.** `ClearMismatchedEmbeddingsAsync` clears `embedding`/
  `embedding_dim` on mismatched rows and re-embeds via the existing
  background backfill; it never runs automatically on a settings or model
  change, and never touches memory content, only its vector cache.
- **1.1's reconcile and 1.6's status-mirroring are internal-state
  corrections with no new execution path.** Reconciling a child's terminal
  status onto its parent's spec, and mirroring a paused child's status onto
  the parent, only ever copy already-persisted, already-approval-gated state
  around; neither can cause an action to execute that would not otherwise
  have been approved.
- **3.1's recent-tasks list and review-queue Open button surface existing
  state, they do not add a new entry point to execute anything.** Opening a
  task loads its persisted state into the workbench exactly as the existing
  review queue already did; approvals and replies still go through the same
  gated paths regardless of how the task was opened.
- **3.3's conversation-delete confirmation and 3.6's Ctrl+Q removal are
  pure friction-adding changes**, consistent with every other
  confirm-gated destructive action in the app; neither has a security
  implication beyond reducing accidental data loss.

## r15: Sub-Task Orchestration

- **No new tool capability, no new network surface, no gate bypass.**
  `plan_subtasks` only ever produces ordinary child tasks that go through the
  unchanged `AgentSafetyGate`/`AgentToolExecutor` path per action; nothing in
  this round widens what a child is allowed to do, and no approval ever
  propagates from parent to child, child to sibling, or via
  `RememberedCommandApprovals` (task-scoped, never inherited).
- **The new attack surface is prompt-injected decomposition**: workspace
  content convincing the model to propose malicious-looking sub-tasks (e.g. a
  sub-task goal worded to make a later step look routine). Mitigation is the
  same as every other gated action: the plan itself is approval-gated with a
  full preview (`AgentApprovalPreview.Describe`, every proposed goal/profile
  visible before approval), each child's own actions still hit the unchanged
  per-action gates individually, and depth-1 (enforced in code in
  `AgentService.RunStepAsync`, not by prompt instruction) prevents a child
  from using its own delegation to launder a bigger blast radius through
  recursive amplification.
- **The standing `02-prompt-injection` scenario still passes** with
  orchestration wired in; the scenario runner's auto-approve hook now applies
  to child pending actions too but still refuses to auto-approve
  `run_command` anywhere in the tree (`AgentScenarioManifestValidator`).
- **`report.md` is written via the existing atomic-write pattern**
  (`AtomicFileWriter`) to the parent's own task directory only, never
  workspace-relative, so it carries the same path-safety guarantees as every
  other agent-owned file.

## r14: GPU Runtime, Serving Defaults, Update Hygiene

- **New download assets keep r11's provenance posture.** The GPU build
  variants (`llama-<tag>-bin-win-cuda-<ver>-x64.zip`,
  `llama-<tag>-bin-win-vulkan-x64.zip`) and the CUDA runtime companion
  (`cudart-llama-bin-win-cuda-<ver>-x64.zip`) are the same
  GitHub-releases-over-HTTPS, tag-pinned-or-latest-API, GitHub-origin trust
  as the existing CPU asset: no independent per-asset hash is published, so
  the exception is origin-integrity, not attestation - the deliberate stance
  first stated in r11. All are extracted through the zip-slip-guarded
  `ArchiveExtractor`. Asset names are matched by os/arch plus a fixed variant
  token, re-verified against the live release API at implementation time.
- **Launch-arg changes stay inside `ArgumentList` (no shell) and bound to
  `127.0.0.1`.** The new flags (`--parallel`, `--cache-reuse`,
  `--n-gpu-layers 999`, embeddings `-b/-ub`) are constant literals or config
  integers, never user-interpolated strings; the loopback host bind and its
  extra-args precedence are unchanged.
- **Version pruning deletes only tag-pattern directories under the resolved
  install root**, is confirm-gated (no confirmation callback means no
  deletion), keeps the current and previous versions, and skips locked
  directories. It never touches a user model or data path. The install-root
  resolver walks up only `bNNNNN` directories and never into a drive root, so
  a legitimately tag-named install root is preserved.
- **No background/auto update polling and no auto-restart without consent**
  (both explicit r14 rejections): update checks and the post-update
  server-restart remain user-initiated, and no accelerator variants beyond
  CUDA/Vulkan are added this round.

## r13: Model Library And Hugging Face Integration

`0.18.0-alpha` adds one genuinely new outbound surface (huggingface.co) and
two new data-mutation actions (the model folder organizer and the model
updater), plus read-only registry queries for honest system info. Everything
below is scoped to that new surface; Local API, MCP, secrets, redaction, and
RAG remain unchanged since `0.9.41-alpha`.

- **New outbound surface: huggingface.co, manual-only.** `HuggingFaceClient`
  only calls `https://huggingface.co/api/...` and
  `https://huggingface.co/{repo}/resolve/main/...` (HTTPS, host-hardcoded,
  no user-configurable base URL). Every call is triggered by a specific
  button press (search, select a repo, download, "Check for updates",
  "Update", "Link to Hugging Face repo..."); none run on startup or a timer,
  matching the r6/r8 posture that Hermaeus's "0 configured outbound
  destinations" claim must stay literally true until the user opts in.
  Access is anonymous only - no HF token, no gated/private repo support (an
  explicit r13 rejection) - so there is no new credential to store or leak.
  `PrivacyAuditService.CountOutboundDestinationsAsync` and `ScanAsync` now
  disclose this surface whenever the model manifest has at least one
  repo-linked entry, so the System page's outbound-destination count stays
  honest the same way it already does for remote chat/voice providers, RAG
  web ingest, and MCP servers.
- **Download integrity is origin-integrity, the same deliberate stance as
  r11's llama-server binary.** Every HF download (starter model precedent,
  browser download, and update) is verified against the SHA256 the repo's
  own tree API reports (`lfs.oid`) before the file is trusted or swapped
  into place. This is integrity against corruption and against
  huggingface.co serving something other than what its own metadata
  describes - not independent third-party attestation, since the hash and
  the file come from the same origin. This mirrors the r8 starter-model
  posture exactly and is recorded explicitly here per the r13 spec's
  request, the same way r11 recorded the equivalent scoped exception for
  the llama-server binary.
- **The folder organizer is move-only and confirmation-gated.**
  `ModelFolderOrganizer.Plan` is pure (no filesystem writes); `ExecuteAsync`
  only runs after the view shows a full "from -> to" preview (including
  every collision it will skip) and the user confirms. It never renames a
  file, never overwrites a name collision, and moves multi-part GGUF sets
  atomically (all parts or none). Leftover empty directories are offered for
  removal as a **second, separate** confirmation, and that removal path only
  ever deletes directories that are still empty at removal time - it never
  deletes a file. The organizer also refuses to start while any managed
  server is running, since Windows holds an exclusive lock on a model file a
  running `llama-server` has open.
- **The model updater's atomic swap never destroys the original on
  failure.** `ModelUpdateApplier.Swap` moves the current file to
  `<file>.previous`, moves the (already hash-verified) replacement into
  place, and only deletes `<file>.previous` once both moves succeeded; a
  failure at the second move restores the original from `.previous` before
  returning, and a leftover `.previous` from a prior interrupted update is
  refused rather than silently overwritten. The update path itself refuses
  to run while the model is running, or while any managed server currently
  running has that file as its `ModelPath`. A hash mismatch after download
  deletes only the `.update.tmp` file and leaves the original untouched. No
  update is ever auto-applied - "Update" is a per-model button press, and
  there is no background update polling (both explicit r13 rejections).
- **Registry reads for system truth (RAM, OS build, CPU name, GPU/VRAM) are
  read-only HKLM queries**, wrapped in try/catch, requiring no new
  privileges beyond what the app already runs with. No WMI (an explicit r13
  rejection: slower and flakier on stripped installs than the P/Invoke +
  registry + `nvidia-smi` combination already in place).

## r12: ViewModels Deep-Dive

`0.17.0-alpha` is the first dedicated audit of Hermaeus.ViewModels. No new
network, process, or secret surface; the two touches below shrink an
existing implicit-trust surface and close a settings-integrity gap.

- **The agent no longer treats the user's whole profile folder as an
  implicit workspace.** `AgentViewModel.WorkspaceRoot` defaulted to
  `Environment.SpecialFolder.UserProfile`, and `LoadAsync` (run at every
  startup and on every Agent panel navigation) unconditionally enumerated
  and analyzed it, writing a "Workspace profile" workspace-memory entry for
  a folder the user never explicitly chose - against the standing posture
  that the agent's workspace is always an explicit user choice. The default
  is now empty; the existing "no workspace selected" empty state governs
  until the user picks a root, at which point the previously-audited
  read-first, approval-gated tool surface applies exactly as before.
- **Trust rescans are now genuinely read-only with respect to settings.**
  `TrustSettingsViewModel.SyncSettingsForTrustScan` used to copy the
  edit-box values (TTS paths, assets root, reranker path) directly onto the
  live, shared `ISettingsService.Settings` object without saving - a scan
  could leave an unconfirmed edit sitting in memory until an unrelated save
  persisted it. Trust scans (and the Local AI setup scan) now build a
  scan-scoped deep copy instead; nothing a scan does can affect what
  eventually reaches disk. The broader settings-lifecycle fix behind this
  (`SettingsViewModel.SaveAsync` applying onto a deep copy, swapped in only
  on success) is a data-integrity hardening, not a new attack surface, but
  is recorded here since it changes how live settings can be mutated.

## r11: Services Deep-Dive

`0.16.0-alpha` is the first dedicated audit of Hermaeus.Services (providers,
process management, stores, setup/download, Doctor, voice glue). No new
network surface; the touches below are to the app's only self-updating
binary download, the data-root migration, and an information-leak-class fix.

- **The llama-server installer is rebuilt.** Previously the only code path
  that downloads and runs a third-party binary. `ArchiveExtractor` extracts
  the real llama.cpp release archive (zip on Windows, tar.gz elsewhere) with
  a zip-slip guard: every entry's resolved destination is checked to remain
  inside the target directory before it is written, and a malicious entry
  (`../evil.exe`, or an absolute path) is rejected outright rather than
  extracted. **Provenance decision:** GitHub's releases API does not publish
  per-asset SHA256 hashes, so the pinned-tag download path is HTTPS + GitHub
  origin + exact tag + exact asset name, not a pinned hash - the same trust
  boundary the app already places in GitHub for its own release channel. The
  latest-release path additionally verifies the selected asset matches this
  platform's expected naming exactly (`SelectDownloadAsset`, tested against
  a captured real release fixture) before downloading. This is a narrower
  guarantee than the SHA256-pinned starter-model and embedding-model
  downloads (r8/Doctor), which remain the standard for content Hermaeus can
  pin a fixed hash for; it is recorded here as a deliberate, scoped
  exception for a binary that moves with every llama.cpp release.
- **The setup wizard's Phi-4 model download closes the last unverified-
  download gap.** `ModelHashes` was an empty map (dead verification branch);
  the download is now pinned via the Hugging Face LFS oid, matching the r8
  `StarterModelCatalog`/Doctor embedding-model precedent, and a failed
  verification deletes the file.
- **Data-root migration now moves `secrets.local.json`/`secrets.local.key`
  along with everything else the app writes to the data root**, closing a
  gap where a moved data root left a live copy of the app's most sensitive
  file behind in the old root while the new root's `SecretStore` silently
  reported missing credentials. The move is a `File.Move` (not copy), so the
  old root retains nothing after migration; the moved files have their
  owner-only Unix permissions re-applied (mirrors `SecretStore`'s own
  `TryRestrictPermissions`) since a move does not guarantee mode bits
  survive across filesystems. `BackupService` shares the same
  `DataRootManifest` enumeration and continues to exclude both secrets files
  from backups by design, unchanged from prior rounds.
- **Runtime-profile health checks no longer send a secret reference as a
  bearer token.** When a runtime profile's API key was stored through
  `ISecretStore`, `CheckHealthAsync` sent the literal `secret:<name>` string
  as the `Authorization` header instead of resolving it - an information-
  leak-class issue (the reference string reaches a network peer instead of
  the credential), low severity and loopback-typical for local runtimes, but
  a real bug for any profile pointed at a remote OpenAI-compatible endpoint.
  Resolved via the same `ISecretStore.ResolveAsync` path every other
  outbound call uses.

## r10: RAG Storage-Destructive Actions and Shutdown Disposal

`0.15.0-alpha` is a RAG-subsystem correctness/quality pass with two
data-destructive or data-rewriting additions and one shutdown-disposal
change. No new network surface.

- **Reindex action** (`RagPipeline.ReindexDatasetAsync`, `RagViewModel.
  ReindexDatasetAsync`). Re-embeds every stored chunk of a dataset with the
  currently configured embedding model and rewrites the dataset's recorded
  `EmbeddingModel`/`EmbeddingDimensions`. Explicit and user-clicked (a button
  shown only when the dataset's recorded model differs from the current
  one); never a side effect of ingest or a background pass. Operates only on
  the app's own `conversations.db` chunk rows - it re-embeds stored content,
  never touches or requires the original source files, and never reaches the
  filesystem outside the data root.
- **Remove missing sources** (`RagQueryService.RemoveMissingSourcesAsync`,
  `RagViewModel.RemoveMissingSourcesAsync`). Deletes chunk rows for source
  files no longer present on disk. Confirm-gated in the VM the same way
  dataset delete is (`RequestRemoveMissingSourcesConfirmation`, a dialog
  listing the exact paths about to be dropped before the user can proceed).
  Never automatic during ingest or a health refresh: a temporarily
  unmounted drive must not silently shred a dataset, matching r9's
  rejection of auto-killing an unrecognized process. Deletes only rows in
  the app's own database; the source files themselves are never touched.
- **Dataset delete no longer relies on a foreign-key pragma.**
  `DeleteDatasetAsync` previously depended on `ON DELETE CASCADE`, but no
  connection ever enabled SQLite foreign-key enforcement, so every deleted
  dataset left its chunks and BM25 stats behind. Deletes are now explicit,
  transactional statements in `SqliteRagStore`, and store initialization
  does a one-time sweep (logged if non-zero) for rows already orphaned by
  the old behavior. This closes a data-retention gap (deleted data was not
  actually deleted), not a new destructive surface.
- **Shutdown disposal.** `App.axaml.cs` disposed the DI container
  synchronously (`sp.Dispose()`), which throws for any singleton that is
  `IAsyncDisposable`-only - `McpToolBridge` is exactly that, so an active MCP
  session produced an unhandled exception on window close. Shutdown now
  awaits `sp.DisposeAsync()` bounded to 5 seconds; a hung MCP child process
  is abandoned to the existing r9 job-object containment rather than
  blocking exit. This changes shutdown disposal only, not what the app is
  allowed to launch or terminate during a normal session. A guard test
  (`ShutdownDisposalTests`) enumerates every singleton registration whose
  implementation type is `IAsyncDisposable`-only against a maintained
  allowlist, so the next async-only service added to the DI graph fails the
  build instead of silently reintroducing this crash.

Explicitly rejected for this round (see `docs/review/archived/r10/04-roadmap.md`):
auto-reindexing a dataset when the embedding model changes, auto-removing
missing-source chunks during ingest, a vector database or persisted
ANN/inverted index dependency, LLM-based query expansion on the query path, a
semantic grounding scorer to replace token overlap, auto-changing
`llama-server` flags from send-lag findings, an async-over-sync rework of
Avalonia's synchronous `Exit` event, and stripping em dashes app-wide (the
new typographic normalization in `KokoroTextNormalizer` is scoped to the
speech boundary only, not authored text or chat rendering).

## r9: Server Lifecycle Hardening

`0.14.0-alpha` closes the process-lifecycle gap the 2026-07-15 crash exposed:
an orphaned `llama-server` held a port, RAM, and GPU layers across app
restarts. Three new surfaces, all confined to processes the app itself
launched.

- **Job-object process containment** (`ProcessJobObject`,
  `Win32ProcessJobObject`). On Windows, every managed server, auto-tune
  probe, and voice-engine (XTTS/Kokoro) child process is assigned to one
  shared job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so the OS
  kills them when the app process dies, however it dies. This is containment
  of the app's own children, not a new attack surface: no new process is
  launched that wasn't already being launched, and job-object creation or
  assignment failure only logs a Warning and never blocks or alters the
  launch. Non-Windows is a no-op behind `OperatingSystem.IsWindows()`.
- **Port-owner lookup** (`PortOwnerLookup`, `SystemPortOwnerLookup`). Before
  launching, and when detecting an orphan, the app reads local TCP listener
  state (`IPGlobalProperties.GetActiveTcpListeners()`, cross-platform) and,
  best-effort on Windows, the PID and executable path of whatever process
  owns a given loopback port (`GetExtendedTcpTable`, read-only). This is
  read-only process metadata inspection - it names a process for the user,
  it never acts on what it finds by itself.
- **Orphan Stop affordance** (`OrphanServerDetector`). This is the only place
  the app terminates a process it did not start this session, so it is the
  most sensitive of the three additions. Mitigations: (1) exact-path
  identification - a process only qualifies as "this server's own orphan,"
  and only then gets a Stop button, when its executable path matches the
  server's configured `ExecutablePath` exactly (normalized, case-insensitive
  on Windows); any other process on the port is reported for information
  only, with no Stop affordance, matching the existing rule that the app must
  never terminate a process it cannot positively identify as its own binary.
  (2) Stopping is never automatic - it requires an explicit user click,
  routed through `OrphanServerDetector.TryStop`. (3) PID-reuse guard: the
  port's owner and its executable path are both re-verified immediately
  before the kill, inside `TryStop` itself, not from a cached snapshot the
  UI took when it first showed the banner; a PID that has since been
  reassigned to a different process, or whose executable no longer matches,
  refuses the stop instead of killing the wrong process.

Explicitly rejected for this round (see `docs/review/archived/r9/04-roadmap.md`):
auto-killing an unrecognized process on a conflicting port, and any
synchronization-based alternative to UI-thread marshaling for the unrelated
`UiBoundCollection<T>` guard work landing in the same release.

## r8: Starter Model Downloads, Clickable Links, Pronunciation Lexicon

Three new download/content-rendering surfaces landed in `0.13.0-alpha`.

- **Starter model downloads** (`StarterModelCatalog`, wired through the setup
  wizard). The catalog is a hardcoded, three-entry list (no user-supplied
  URLs); every entry is `https://` and carries a pinned SHA256 verified via
  the existing `ModelDownloadService.VerifyHashAsync` before the file is
  trusted. A hash mismatch deletes the downloaded file and reports an error;
  the app's settings are left untouched. No new download primitive was
  introduced - this reuses the same verified path `DoctorService`'s
  reranker/embedding-model installs already use.
- **Clickable markdown links** (`MarkdownViewer`). Assistant output can now
  open the user's default browser on click. Mitigations: a scheme allowlist
  (`IsSafeLinkScheme`, `http`/`https` only - `file:`, `javascript:`, `data:`,
  and anything unparsable render as inert styled text, never launched), an
  explicit user click is required (nothing auto-opens), and the full target
  URL is shown in the link's tooltip before the click happens.
- **User pronunciation lexicon** (`{DataRoot}/voice/lexicon.txt`). Plain text,
  parsed defensively: every IPA value is validated against Kokoro's fixed
  vocabulary symbol-by-symbol before being accepted; a line that fails
  validation is skipped and logged, never executed, interpolated, or passed
  to a shell. The file only ever affects locally-synthesized speech text.
