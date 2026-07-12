# 01 - First Five Minutes

Goal: each of the seven questions gets an answer that is visible where the
question arises, not buried in a settings page. Current state below was
verified against `0.10.0-alpha` code during planning.

## Current state (verified)

- Navigation is icon-only with tooltips
  (`src/Aether.Desktop/Views/MainWindow.axaml:40-134`): Chat, Agent, RAG,
  Models, Services, Benchmarks, System overview, Doctor, Memories, Logs,
  Notifications, Quick chat, Settings. A new user cannot see what the app
  contains without hovering 13 icons.
- First-run wizard exists and auto-shows
  (`MainWindowViewModel.cs:141`), and its first step is the data root, so
  question 1 has a decent story at setup time.
- Settings > Data shows the data folder path
  (`SettingsDataSectionView.axaml:10`) plus the restore caveat text.
- Privacy Audit is an expander inside System overview
  (`SystemOverviewView.axaml:47`); Trust checks live in Settings > Trust.
  Nothing at point of use indicates whether the currently selected chat
  model or voice provider is local or remote.
- Per-message model attribution was re-audited during implementation and
  turned out to already work end-to-end: `Message` (Core) already carries
  `ModelId`/`DurationMs`, `ChatViewModel.SendAsync` sets them per assistant
  message, `SaveAsync`/`LoadConversationAsync` round-trip them through
  `messages_json`, and `MessageViewModel.MetaDisplay` renders them after
  reload. The planning-time note above conflated this with the separate,
  genuinely-live-turn-only `Sources` collection comment
  (`MessageViewModel.cs:30-37`), which is about memory/RAG citations, not
  model id. No code change was needed for 1.4; see
  `ConversationStoreRoundTripsPerMessageModelAttribution` in
  `src/Aether.Tests/ServiceTests.cs`, added to lock in the behavior.
- RAG citations show a bare composite score
  (`RagViewModel.cs:25`, `ScoreDisplay => $"{Score:F3}"`); nothing explains
  what the number means or which retrieval signal produced it.
- Agent risk is shown as a bare word at 11px / 0.55 opacity
  (`AgentView.axaml:301`); the safety gate produces a human-readable
  `Reason` for every decision (`AgentSafetyGate.cs:33-84`) but only
  `BlockReason` for blocked patches ever reaches the UI
  (`AgentViewModel.cs:188-213`).
- There is no undo for an applied patch and no aggregate "what deleting my
  data covers" statement. Backup/restore exists in Settings > Data.

## Items

### 1.1 Navigation labels

Give the sidebar an expanded mode showing icon + label for every entry,
either always-on (preferred, matches typical IDE tool strips) or toggled by
the existing hamburger (`MainWindow.axaml:31`), persisted in UI settings.
Keep tooltips.

Acceptance criteria:

- All 13 destinations readable without hovering.
- Collapse state survives restart (settings round-trip test).

### 1.2 Where is my data stored?

- Add an "Open folder" button beside the data folder and AI assets rows in
  Settings > Data (launch via `Process.Start` with `UseShellExecute = true`
  on the directory path only; never a user-typed string).
- System overview gets a one-line "Data root: <path>" row with the same
  open button, so the answer also lives outside Settings.

Acceptance criteria:

- Both locations show the live resolved data root (respecting a configured
  override), not the default.

### 1.3 Can anything leave my machine?

- Chat header: a small badge next to the model selector reading "Local" or
  "Remote endpoint" derived from the active backend kind (managed
  llama-server / local Ollama on loopback = Local; user-configured remote
  OpenAI-compatible endpoint = Remote). Tooltip names the host.
- Voice: when the active voice provider is remote (OpenAI voice), Settings >
  Voice shows an inline note on each enabled non-Chat channel that its
  utterance text is sent to the remote provider (see also item 3.4).
- Promote Privacy Audit: rename the System overview expander to "Privacy
  audit - what can leave this machine" and default it expanded (it already
  is; keep it) and add one summary line at the top: "N configured outbound
  destinations" counting remote endpoints, web-ingest-enabled RAG datasets,
  remote voice, and MCP servers.

Acceptance criteria:

- Local model selected: badge says Local; switching to a remote endpoint
  flips it without restart.
- Summary line count matches the audit items shown beneath it (unit test on
  the counting logic).

### 1.4 Which model answered this? (already implemented, verified)

Turned out to already work end-to-end (see "Current state" above): no code
change needed. Verified all three acceptance criteria against the real
`ConversationStore` in `ConversationStoreRoundTripsPerMessageModelAttribution`
(`src/Aether.Tests/ServiceTests.cs`):

- New assistant messages show "model - duration" after app restart.
- Pre-r6 conversations (messages_json with no modelId key) load unchanged,
  no exception, meta line simply absent.
- Mid-conversation model switch: each message keeps the model that
  actually produced it.

### 1.5 Why were these files selected? (agent context receipt)

`AgentContextBuilder` injects memory, RAG, instructions, and lessons under
per-section token budgets (`AgentContextBuilder.cs:9-16`) but the user never
sees what was injected. Add a per-step "Context" disclosure in the agent
transcript UI listing, per section: item count, token estimate, and the
item identifiers (memory titles, RAG source labels, lesson claims,
instruction file names). Read from data the builder already assembles; no
new persistence beyond including the receipt in the existing transcript
entry payload.

Acceptance criteria:

- A task step whose prompt included 2 memories and 1 lesson shows exactly
  those, with counts.
- Sections that contributed nothing are omitted, not shown empty.

### 1.6 Why did retrieval choose this chunk?

Replace the bare `{Score:F3}` in the citation detail pane with a
breakdown from the retrieval pipeline: which signals matched (vector
similarity, keyword match, reranker) and each component score, plus one
plain-language line, e.g. "Ranked 2nd of 8: strong semantic match, term
'migration' matched 3 times, reranker agreed." If a component was not run
(reranker disabled), omit it. Extend `RagQueryTrace` detail rather than
inventing a parallel structure, and show the same breakdown in the RAG
query trace view.

Acceptance criteria:

- Citation pane shows component scores for a hybrid query.
- Vector-only configuration shows only the vector component, no blanks.
- Plain-language line is deterministic from the components (unit test).

### 1.7 Why was this patch flagged as risky?

- Surface the safety gate's `Reason` string wherever a risk level is shown:
  patch review rows, tool approval prompts, and the recipes list. Stop
  hiding risk at 0.55 opacity; render Medium/High as a visible chip.
- Command approvals additionally show what the command transitively
  executes (item 3.2).

Acceptance criteria:

- A pending patch shows both the risk chip and the gate reason text.
- An mcp: tool approval shows the "MCP tool calls always require approval"
  reason.

### 1.8 Can I undo everything?

- Applied-patch revert: when `apply_draft_patch`/`edit_file`/`create_file`
  mutates a file, store the pre-image (or "absent" for created files) next
  to the task state, and offer "Revert" on the applied patch entry that
  restores it, guarded by the same baseHash staleness check in reverse: if
  the file changed again after the apply, warn instead of blindly
  restoring. Retention: pre-images live with the task and go away when the
  task is deleted.
- Add a short "Your data, your machine" block at the top of Settings >
  Data: three sentences stating everything lives under the data root,
  what backup covers (and the existing secrets caveat), and that deleting
  the data root removes all Aether state except OS-keychain secrets.

Acceptance criteria:

- Apply then revert restores byte-identical content; created files are
  deleted on revert.
- Revert after an external edit warns and does not overwrite.
- Pre-r6 task state without pre-images loads fine; those entries simply
  show no revert button.
