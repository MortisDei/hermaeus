# 08. Desktop product UX and information architecture

## 8.1 Audit basis and priorities

This is a source-grounded product workflow audit, using views, bound commands,
view models, current workflow docs and the supplied dogfood observations. No
live Desktop session was launched. Workflow/control findings are RF; judgments
about density, focus and discoverability are IN pending owner walkthrough.
Static XAML cannot prove actual DPI, focus, accessibility or rendered spacing.

**P outcome:** each major surface makes the current job, target, state and next
action obvious. Preserve native Desktop, existing behaviors, approval boundaries,
light/dark themes and semantic resources. No alternate-theme or wholesale visual
redesign round. Prioritize loss of work, incorrect action targets and misleading
state over decoration.

## 8.2 Agent: finish the work and inspect what happened

**Current:** four useful tabs already exist. A global decision strip reaches
approvals; Run contains goal/workspace/model/RAG setup, outcome, response, reply,
continuation, subtasks, plans and state disclosure. Workspace places management
content before a `320,*` file browser/editor region in a width-capped scrolling
stack (`AgentView.axaml:827-1110`). Changes derives from patch/ledger state.

**Problem:** the user must distinguish run progress, approval, filesystem change
and artifact completion. Raw state and management compete with primary work;
a substantial artifact can remain a small preview. Tabs alone did not repair the
underlying outcome mismatch or transient state lifetime.

**P:** retain Run/Changes/Workspace/History. Run centers on execution phase and
next owner decision; setup collapses to a labeled target summary after starting.
Changes shows prepared proposals separately from observed/verified changes, with
a before/after inspection and clear stale/refused/no-effect outcomes. Workspace
provides the main resizable artifact editor/viewer, with management behind an
explicit action. History owns transcripts/receipts and cross-task navigation.
Completion presents a primary artifact action and unresolved criteria, rather
than requiring the user to search a narrow secondary pane. Keep approval context
visible and do not auto-switch away while the owner is reviewing a decision.

Persist workspace/model preferences; persist task outputs with the task; clear
task-scoped response, follow-up, source and selected-artifact state on New Task.
**Dependencies:** doc 03 receipts and isolation; editor choice is replaceable.
**Verification:** owner can identify what changed, whether it was verified and
what remains, then inspect the artifact at full usable width without losing
approval context. V01-V05 and owner DPI/keyboard/resize checks.
**Non-goals:** adding more top-level Agent tabs, terminal, debugger, Git UI.

## 8.3 RAG: ask, add knowledge, manage sources

**Current:** Ask/Sources/Diagnostics/Manage navigation already exists. Dataset
selection controls ingest/reindex/evaluation; multiple datasets can independently
scope a query. The view explicitly explains the difference. Ingest Documents and
Eval Harness remain large expanders under the query content
(`RagView.axaml:121,326,487`); Sources means evidence from the last answer, while
source management is elsewhere. Dataset Manager already supports source/history
work; atomic generations and temporal provenance are implemented.

**Problem:** selected dataset has two neighboring meanings, Sources sounds like
management but means retrieved evidence, and adding documents is buried in the
question workflow. User has to remember control scope rather than follow a job.

**P:** retain existing query and management operations. Make question scope a
visible part of Ask; rename/clarify retrieved Sources as answer evidence. Give
Add documents and Manage knowledge/source health explicit entry points using the
same selected dataset identity. Put evaluation with diagnostics/advanced tools,
not in the ordinary ask-and-answer reading flow. Provide contextual actions from
an unhealthy/missing/stale source to its relevant manager row. Ingest progress,
last result, current published knowledge generation and query evidence are
separate state; cancellation must not erase the current usable knowledge.

Do not add a separate Knowledge database or navigation hierarchy just to change
labels. Keep destructive source/dataset removal review distinct from query scope.
**Dependencies:** existing RAG generation contract and shared operation/navigation
contracts. **Verification:** owner adds/refreshes/removes a source, sees health,
asks across two datasets, opens cited revision, cancels ingest, and returns to
the same context without changing an unrelated dataset. V12 and owner matrix.
**Non-goals:** GraphRAG, new retrieval engine, broad web crawler, graph UI.

## 8.4 Lab: find a useful test, then make a decision

**Current:** Experiment combines manual candidate context, start/complete/cancel,
frozen JSON, recipes, prompt/tradeoff table and Apply. Evidence has many domain/
outcome/origin/status/Project/fingerprint filters and selected detail; historical
review is read-only. Recipe state reasons already exist. See LabView lines
19-110 and 116-266, and doc 06.

**P:** make baseline identity first, then Available/Unavailable/Unknown experiment
discovery. Use a selected experiment detail pane for its purpose, changed
variable, prerequisites and correctness requirement. Running shows progress,
Cancel and source-restore status. Results lead with comparison and correctness,
then Review changes or no actionable result. Keep manual definition and raw
fingerprint filters under Advanced. Details should open retained evidence by id,
not a generic page. No fake best candidate when evidence ties or is incomplete.

**Dependencies:** doc 06 reconciliation and lifecycle. **Verification:** owner
can answer what is testable, what changed, whether evidence is valid, and whether
Apply affected settings or the loaded runtime. V07/V08, owner cancellation/restore.
**Non-goals:** removing scientific detail or creating a separate beginner Lab.

## 8.5 Rest-of-product sweep

| Surface and primary job | Current evidence / UX finding | Proposed disposition and acceptance |
| --- | --- | --- |
| Services: run/configure a capability | Per-row editable fields, saved config, running state, fit/tune and recommendations coexist; external Apply can leave editor fields stale. | Mandatory: label configured/editing/running differences and dirty conflicts, reveal target row from Doctor/Lab. Verify next Start uses reviewed values. |
| Models: choose/manage a model | Model profile defaults and saved tune profiles are shown near runtime actions (`ModelManagementView.axaml:119-163`). | Mandatory with runtime work: show which values are preferences/evidence and which require Services save/restart. Do not imply a tune profile automatically governs a running model. |
| Settings: preferences and storage | Long mixed grid contains LLM/RAG/voice preferences, data/setup, UI/memory, MCP/Local API/trust (`SettingsView.axaml:25-42`); persistence/error status is at bottom. | Targeted navigation and persistent save/error indication earn inclusion. Group or disclose sections when touched, keep current domain placement rules. Do not reorganize every setting. |
| Doctor: resolve a problem | Category navigation opens a page, not the relevant field/server; action kind derives partly from displayed label. | Mandatory typed targeted remediation, doc 09. Owner reaches the actual control, or sees why no direct remediation exists. |
| Benchmarks: compare evidence | Existing suite scoring/insights are useful; cancellation can be relabeled Ready or followed by completion toast. | Mandatory outcome clarity with V06. Keep quality/latency/throughput comparable only under declared identities. |
| Chat: converse and inspect generated content | Markdown code now uses readable selectable text after prior virtualized editor blanking; context and response are primary. | Preserve known readable path. If artifact inspection is touched, use shared explicit artifact navigation, not another editor nested in message virtualization. |
| Memories: review stored assertions | Revision timeline and contradiction review already exist; pinned/other lists and filters support inspection. | Preserve; no new knowledge engine or universal truth score. Include existing revision/citation behavior in smoke scenarios. |
| System Overview: explain resource pressure | R32 resource snapshots and Unknown-aware attribution exist. | Link a resource conflict to consumer/operation details using stable ids. Do not relabel whole-device totals as per-process VRAM. |
| Activity/Logs: understand prior work | Existing activity/provenance/log surfaces | Use operation ids and targeted details, bounded text and origin filters for JSONL. No parallel diagnostics dashboard. |
| Projects/navigation: choose context | Project and task identities already exist; active panel is a string callback in MainWindow. | Freeze task identity, preserve navigation context and introduce typed target payload where needed. No global workspace-graph abstraction. |
| Voice/setup/storage confirmations | Existing explicit controls and owner-anchored dialogs | Keep authority and safe restart/migration semantics. Only targeted lifecycle/navigation changes; no repeated destructive migration for evidence. |

For substantial items in this table, owning contracts/dependencies are docs 02,
05-07 and 09. Items marked preserve are not additional implementation batches.

## 8.6 Control and presentation acceptance rules

Use tabs/section navigation for destinations, selection controls for choosing
objects, toggles for binary preferences and explicit buttons for actions. A
navigation choice must not silently save configuration. Every page should retain
its contextual target on return, while task/operation-bound results must reject
late updates for an old target. Keep primary outputs in primary space; raw JSON,
implementation ids and detailed filters remain accessible behind disclosure.

Replace touched raw colors with existing semantic resources where practical.
A bounded cleanup belongs only to touched views; no color-system rewrite.
Source tests enforce bindings/tooltips and structural invariants. Owner checks
must cover focus, keyboard, screen/DPI scaling, scroll, empty/failed/running/
completed states, light/dark contrast and whether the workflow is understandable.

## 8.7 R33 continuation reconciliation

The current implementation applies the bounded findings above rather than
repeating the old surface audit:

- Agent keeps the four-tab direction and decision strip, but now leads with
  readable lifecycle labels, approval context, explicit prepared/applied/
  verified/conflicted outcomes, a clean New Task message, child and plan
  labels, run-artifact navigation, and a primary Workspace editor. Action
  groups wrap at narrow widths. The editor prefers local Monaco and falls back
  to AvaloniaEdit without changing Agent authority.
- RAG keeps the existing atomic ingest, retrieval, citation, trace, and Chat
  Knowledge architecture. Ask now says whether it is ready, searching,
  successful, refused, failed, cancelled, or missing a knowledge base, and
  names the next action. Local files versus Remote web is shown at dataset
  scope. Sources and Diagnostics remain secondary inspection destinations, and
  their controls plus evaluation actions wrap on narrow layouts.
- Lab keeps its existing isolated run, recipe, evidence, and Apply lifecycle.
  Capability and recipe availability are described in user terms, actions are
  disabled when the current server/model cannot support them, filters and raw
  recipe detail are disclosed, and run/restore/Apply next actions remain
  visible. Narrow action groups wrap.
- The optional ChatGPT Pet v2 support is a data-only, disabled-by-default
  ambient overlay. Generic package validation, clamped persistent position,
  and a bundled Moss package are implemented without adding chat, voice,
  filesystem, or script authority.

Automated tests and source inspection cover these projections and safety
boundaries. No native Desktop surface was available for this continuation, so
focus, keyboard/IME, resize, DPI, contrast, WebView/worker startup, pet drag,
and complete Agent/RAG/Lab owner walkthroughs remain explicitly open.
