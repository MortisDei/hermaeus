# Hermaeus User Guide

This guide is for the release archive. You do not need the source tree or an
IDE to use Hermaeus.

## Launching the Linux archive

Double-click `Hermaeus` in the extracted directory to launch it without a
terminal. To add Hermaeus to the application menu with the canonical Moss icon,
double-click `Install Hermaeus` and confirm. To remove that installed copy and
application-menu entry later, double-click `Uninstall Hermaeus` in the extracted
archive. Both actions use native launchers and graphical confirmation; neither
requires a terminal, root access, or a file-manager preference for executing
text files. Source and Debug launches may show a generic taskbar icon when the
desktop entry has not been installed; launch the installed entry to verify
release icon association.

## Launching the Windows archive

Extract the ZIP and double-click `Hermaeus.exe` at its root. This small native
launcher exists only to provide a clean entry point while the actual application
and runtime files remain under `app\`. It resolves and starts only the bundled
`app\Hermaeus.Desktop.exe`; it does not install, elevate, update, access the
network or registry, or persist anything. Its source is included in the
repository. Keep the package directories together when moving the extracted
archive.

## First launch

Onboarding asks for two locations:

- **Data Root** stores conversations, memories, Knowledge indexes, agent task
  state, traces, logs, and backups.
- **AI Assets** stores large replaceable files such as GGUF models, managed
  llama.cpp builds, embedding models, and local voice assets.

Keep Data Root on reliable storage. AI Assets can live on a larger drive. If
you change Data Root later, use the in-app migration flow rather than moving
live database files by hand. After entering or choosing a different root,
review the current and destination paths and choose **Move data...**. Hermaeus
asks for confirmation before moving an existing workspace; ordinary Settings
autosave never performs that migration implicitly.

Choose a chat backend next. For managed llama.cpp, use **Install managed
llama.cpp** before reaching Doctor. You can then choose an existing GGUF or
download one of the small, SHA256-verified starter models. Voice is optional;
native Kokoro can be installed during onboarding or later through Doctor.

## Models and Services

**Models** groups the catalog by purpose: **Chat & Generation**, **Embeddings**,
and **Rerankers**. GGUF files and models reported by configured providers are
classified using provider configuration, dedicated asset layout, GGUF metadata,
and trusted manifest provenance. A Hugging Face source badge means Hermaeus
retained download provenance. Open a
model card to edit its display name and per-model defaults or to inspect its
source. When auto-tune has a current profile for a local GGUF, the card shows
the effective tuned GPU layers, threads, and context directly. Open the card's
configuration to inspect and intentionally edit those same saved tune values;
the editor stays within the available window area and scrolls when its bounded
form does not fit. **Save model profile** persists them with the picker defaults
and metadata. Local cards use a detailed GPU-fit prediction when GGUF shape
metadata is available. Provider and download cards show a clearly labelled
pre-download estimate until the file is available locally. These are
projections, not proof of the placement a runtime will eventually select.
Extra arguments and live process overrides remain on Services, where their
trust checks and process state are visible. Runtime process settings still use
**Save Config** on Services.

Factual capability badges such as **MoE**, **MTP**, **Draft**, and
**Vision / Projector** describe model metadata only. They do not mean that a
feature is configured, available, or active. A primary generation card owns
its proven projector, draft, MTP, EAGLE, tokenizer, or sidecar companions;
expand **Companions** to inspect Present, Missing, Stale, or Unknown state.
Companion files are not promoted to independent cards just because they look
like model files. The filter also searches role, capability, tag, and
companion state.

**Services** owns processes and files on disk. A managed llama.cpp server needs
the resolved `llama-server` executable, a GGUF model, a localhost port, and
launch settings. Start it there, then select its model in Chat. Runtime Logs
show the exact startup stage and sanitized process output.

Managed llama.cpp installs and updates honor the configured backend. When the
setting is **Auto**, Hermaeus re-evaluates the current hardware whenever an
installation is required, prefers the hardware's primary accelerated backend,
and may select another compatible accelerated asset when upstream does not
publish that preferred package. The selected backend is recorded separately as
the last installed backend; Auto itself remains Auto. If no compatible GPU
asset is available or the selected build fails its launch probe, Hermaeus
refuses the update with an explanation rather than silently replacing it with a
CPU build. CPU is still available when selected explicitly. Fresh managed
archives are stored under one Hermaeus build directory; older nested archive
layouts remain discoverable for repair and pruning.

The Data Storage panel shows the configured request and the last installed
backend separately. The latter is installation history, not a replacement for
Auto and not proof of which executable a currently running process uses. The
active process identity remains tied to the Services executable and Chat
runtime telemetry.

Capability status is evidence-scoped. `Available` means the selected runtime,
and the selected model where relevant, advertised or demonstrated the feature.
`Unavailable` requires a successful authoritative probe. A failed probe is
`Unknown`. In particular, finding NextN metadata and a generic `draft-mtp` flag
does not prove that MTP engages for that model.

Normal fields on Settings persist automatically after editing and show a small
Saving, Saved, or Failed state. A pending edit is flushed during clean app
shutdown. Reset still discards unpersisted edits. Model and runtime forms on
Services keep explicit Save Config actions because those changes can launch or
reconfigure a process. A missing primary model,
projector, or draft companion is shown as missing and is never silently
replaced by another file.

A stale configured draft path is not a candidate. If the primary model still
has a trusted repository mapping, use its model-card companion review to see
the current hash-verified candidates and explicitly reacquire one before
selecting it in Services. If no trusted candidate exists, clear the stale path
or choose a companion you have independently verified.

Services keeps the configured projector path and the **Use projector** choice
separate. Turn the choice off to stop passing `--mmproj` to `llama-server`
without deleting the saved path or its companion provenance. Turn it on again
to use that configured path, subject to the same verification and missing-file
checks.

For Hugging Face models, companion handling prefers a SHA256-verified
`.hermaeus/companions.json` mapping, but does not require third-party
repositories to add one. Existing `mmproj*.gguf` siblings and
`MTP/mtp*.gguf` files are examined using same-revision tree/LFS data and
bounded GGUF metadata. Only a unique candidate with deterministic role and
model compatibility evidence is selected automatically. Ambiguous or
incomplete candidates are shown unchecked for explicit review; a filename
alone is never enough.

The initial download offers known projector and MTP files individually and
shows their additional size. Each model's **Automatically manage known
companions** setting controls later updates. Disabling it asks whether to Keep
files, Remove files, or Cancel; removal is never implicit. If a known
companion goes missing or stale, the model card reports whether a verified
compatible replacement can be reacquired. Use **Reacquire known companions**
when it can. If no verified replacement exists, the card says so and offers
only the Services path for **Browse** or **Clear projector**. Recovery resolves
the current repository revision and hash-verified compatible candidates, but it
never changes a server's configured projector or draft path. Selecting a
replacement in Services remains an explicit user action.

When the model card declares a Hugging Face thumbnail, the selected repository
and its download cards may show it as optional repository artwork. Hermaeus
reads only the bounded `cardData.thumbnail` value, requires an exact immutable
repository revision, blocks arbitrary external hosts, and falls back to the
generic mark for missing, invalid, unavailable, or unsafe artwork. Artwork
does not affect model identity, fit, trust, ranking, or download selection.
The cache is rebuildable and excluded from Data Root backups. Settings >
Data Storage shows its size and provides a confirmed Clear action that does
not remove models or manifests. A custom model avatar remains separate and
takes precedence over cached repository artwork.

When a repository is selected, known GGUF variants appear immediately while
fit and companion checks complete independently per row. A row remains
download-disabled while its compatibility check is still running.

The server card's **GPU Fit** text is a prediction for the values currently in
the editor, including unsaved changes. It lists weights, K/V cache, runtime
overhead, companions, and headroom separately. `Unknown` means a material input
or trustworthy measurement is missing; it is not treated as zero. Runtime
observations remain separate and are comparable only under the exact v2
runtime/model/hardware/configuration fingerprint.

**System Overview** also shows a whole-workload resource snapshot. It lists
registered consumers and their active allocations, whole-device memory totals,
and Unknown observations that prevent false precision. Each managed server's
Services card shows the admission receipt used for its start. Reservations are
short-lived concurrency guards only. They do not stop or unload another
consumer, change settings, or attribute a whole-device total to one process.

Managed server cards also expose an **Adaptive launch** envelope. It is **Fixed**
by default. **Advise** computes and displays bounded alternatives without
starting one, while **AdaptAtLaunch** may retry a resource-exhausted start with
only the explicitly enabled compromise fields. GPU-layer reductions preserve
an accelerated backend, context reductions stay above the configured minimum,
and KV or CPU-MoE changes require selected-runtime and quality evidence.
Every attempt obtains a fresh whole-workload reservation. If effective context
or placement cannot be audited through structured runtime output, the attempt
stops visibly rather than guessing or falling back to CPU. The transient launch
candidate is never saved over the configured server values. A recent compatible
successful launch may be preferred only when the exact runtime, model, complete
hardware, base configuration, workload identity, and evidence age match. The
current resource snapshot and admission checks still apply.

If onboarding is already complete and Chat reports that no chat model is set
up, go to **Services** to configure or start the current model server. A stopped
server does not reset onboarding. The setup wizard remains available from
Settings when you intentionally want to rerun it.

Ollama and OpenAI-compatible endpoints use runtime profiles. Remote providers
receive the prompts and context sent to them. Hermaeus never turns a remote
provider into a local one merely because the desktop app itself is local.

## Doctor and remediation

**Doctor** checks real paths, executable runnability, services, models, storage,
RAG, and voice readiness. A failed check does not silently change the machine.
Where Hermaeus can remediate a problem, inspect the plan and explicitly approve
the download or write. Details remain available in Doctor, Activity, and
Runtime Logs.

If managed llama.cpp is missing, use onboarding's install action or Doctor's
download action. Hermaeus selects the newest compatible b-numbered upstream
build and links a successful installation back to Services. When an update is
complete, **Remove old llama.cpp versions?** only offers owned, genuinely
superseded builds. The selected build remains protected, including archives
whose executable is nested below the b-numbered directory.

## Chat and context

Choose a model at the top of **Chat**, type a message, and send. Stop cancels an
active generation. Regenerate creates a branch rather than destroying the
previous answer. Deleting the active conversation returns Chat to a fresh,
focused input. Delete from a conversation's details flyout shows its nearby
confirmation; the context-menu path keeps a full confirmation dialog.

While a response streams, scrolling upward pauses bottom-following. Scroll back
to the bottom to intentionally re-pin. The telemetry flyout can start bounded
sampling for the exact managed server process serving the selected model.
Nested panes keep wheel input when the pointer is over their content and pass it
to the page only at an edge. Horizontal overflow remains available in panes
that provide it.

Attach text, code, logs, CSV/TSV, markup, configuration, PDF, DOCX, or supported
image files from the attachment control, drag and drop, or clipboard. Images
are sent only when the selected route actually accepts them. The **Context
Inspector** shows the environment context, prompt, draft, history estimate,
attachments, and attached Knowledge context. Normal Chat does not expose web
access, a shell, tool calls, or Agent workspace actions.

## Projects and Project State

Use the header project switcher or `Ctrl+K` to create and switch Projects. A
Project supplies defaults for new Chat conversations, Agent workspace setup,
and Knowledge selection. Existing work is not silently rebound when you switch.

Open an existing Project's editor to maintain its optional State: current
objective, milestone, status, accepted/rejected decisions, constraints,
unresolved questions, important artifacts, and next actions. These fields are
user-owned and directly editable. Proposed updates stay in a review queue until
you inspect, optionally edit, and accept them; rejecting one does not alter the
accepted revision. Stale proposals are refused after another edit lands.

Accepted State may be included in bounded Chat and Agent context when that work
is bound to the Project. Expand the context receipt to see the separate Project
State section and revision. Pending or rejected proposals are never treated as
accepted context. Project State remains separate from Memories, Recall,
Knowledge/RAG, conversations, and Agent task history.

## Knowledge, Memory, and Recall

**Knowledge** ingests files into a local RAG dataset. Attach a dataset to a Chat
conversation from the Knowledge picker. Retrieval is bounded and cited; weak
matches are omitted instead of forced into every answer. Reindex after changing
the embedding model. Dataset Manager shows the published generation history,
while ordinary retrieval uses only the current complete generation. A cancelled
or failed ingest leaves the prior generation in place. Removing missing sources
requires separate confirmation and publishes a replacement generation rather
than deleting live rows mid-ingest.

**Memories** are durable, reviewable facts stored under Data Root. Settings
control whether memory and Recall context may be injected into Chat. The Chat
environment description reports only enabled context sources. The command
palette can search the local Recall index even when Recall injection into Chat
is disabled. A pinned memory remains visibly marked in its row and exposes
**Unpin** directly, so the state does not depend on a toast.

Open **History** on a memory to inspect its immutable revisions. Recorded time
and established effective time are shown separately, alongside adjacent
content diffs, sources, decisions, and status. **Revise fact** and **Correct
fact** create successors; pinning, tags, archive, and scope changes remain
presentation edits. **Restore as new revision** copies selected historical
content only after an explicit review. A contradiction proposal records two
exact revisions for review and can be rejected without changing either one.
Normal Chat uses only the accepted current projection and names its exact
revision in the context receipt. **Export history** writes bounded, redacted,
versioned JSON containing the visible memories' revision, source, effective-time,
and decision structure. The older CSV action remains a current-projection-only
export, and files exported before deletion remain user-owned copies.

## Agent workspaces

The **Agent** is separate from normal Chat. Select a workspace root, review its
scope and proposed actions, and approve gated operations explicitly. Task state
and patch queues remain inspectable. Workspace authority does not carry into
another workspace or into ordinary Chat.

When a proposed plan contains sub-tasks, its review card has one model selector
per child. Choose a configured visible model or **Inherit parent** before
approving. Hermaeus persists the approved identities, runs each child on its own
selection, and returns final synthesis to the parent's model. Changing the main
model picker later does not retarget an existing task. If a task's frozen model
is no longer available, the task pauses without fallback; select an available
model and use **Use for task** to record an explicit change before continuing.

The optional Local API does not expose Agent task execution in this release.
Its capabilities response reports Agent unavailable because the Desktop and
Local API processes do not yet share one safe task-mutation owner. Named API
tokens cannot create, start, steer, continue, approve, or deny Agent work.

## Live telemetry and audio feedback

Chat's bar includes a compact telemetry flyout for request-level timing and
matching process counters when a local runtime sampling session is available.
The flyout does not replace Chat with a dashboard. `Unknown` means the current
runtime has not supplied trustworthy evidence, not zero. Health conditions are
restrained and deduplicated; high GPU use by itself is normal and produces no
warning.
Request timing labels use first content, meaning the first non-empty content
delta received from the runtime, rather than a provider reasoning or tool
event.

Settings > Voice contains supplementary audio feedback controls for the
explicit task/runtime/recording event list. Volume is retained when muted,
visual notifications remain authoritative, and cues are suppressed while TTS
speaks by default. Playback failure does not fail the operation that raised the
visual notification.

When Recall injection is enabled, the Chat trace identifies keyword-only
fallback retrieval separately from embedding-backed retrieval. Lexical hits
remain usable, but their presence does not claim that semantic retrieval is
healthy.

## Lab experiments and evidence

Open **Lab > Experiment**, select a configured Chat server, name the run, and
set the bounded candidate values shown by the editor. **Freeze and start**
captures the exact definition and starts a separate runtime on a temporary
loopback port. A running selected Chat source is fully stopped and awaited
first, then restored after the run only if its complete configuration is
unchanged. Lab does not save Services settings. **Finish run and save baseline**
records the current shell health observation and cleans up the temporary process.
It is an intentional manual completion step, not an automatic workload or
candidate comparison. **Cancel** stops only the runtime owned by that run and
records the cancelled or partial result. A second manual or recipe run is
disabled while one is active, and the service rejects concurrent callers at the
backend boundary as well.

The run state names isolation and comparison refusals. Missing counters remain
missing. A comparison cannot show a headline delta when runtime, model,
hardware, or configuration fingerprints differ, and a deterministic output
difference fails correctness regardless of speed.

On **Lab > Evidence**, an empty pane says whether no evidence has been captured
yet or whether the current filters exclude existing records.

**Review eligible candidate** lists the exact Services fields that would change.
A completed result names its experiment and presents one top-level Evidence
entry for that execution. Its result card leads with the experiment, recorded
model identity when available, result status, timestamps, tested configurations,
recommendation state, correctness, throughput, and RAM/VRAM deltas. Observed
peaks are labelled separately from predicted values, and missing measurements
remain `Unknown`. Its drill-down retains the baseline, candidates, slices,
provenance, and raw detail. The summary states the only eligible candidate or
explains why no recommendation is available. A
speed-only, uncontrolled, missing-correctness, or stale result is refused.
Guided recipes select the eligible candidate from the completed result
automatically; individual evidence slices do not need manual saving before
review.
**Confirm reviewed changes** asks once more in a modal owned and positioned over
the Hermaeus window, rechecks the selected server plus runtime/model identity,
and saves through the normal Settings path. Review is separate from running an
experiment, and experiment evidence is retained.

Services shows the same review card when an auditable adaptive launch or Lab
result produces a managed-server recommendation. The card separates current
and proposed values, evidence, trade-offs, and freshness. **Apply** saves the
reviewed settings only. It does not restart a running server. **Undo** restores
the bounded pre-Apply fields only when the target has not changed since Apply;
otherwise the service refuses without overwriting the later edit.

Benchmark **Insights** may show a model-guidance card when usage and comparable
benchmark evidence disagree. It is review-only: **Dismiss** suppresses that
identical proposal, and **Open Models** takes you to the model page. It never
changes the selected model.

Managed server GPU placement is edited as CPU, Auto, All, or Exact. The setting
is a request, not a claim about effective runtime placement. Auto is available
only when the selected runtime proves both automatic placement and fit support;
unsupported or unobservable behavior stays unavailable or Unknown. Matching
Auto-tune profiles are evidence and are not silently applied when a server
starts or a model is selected.

Choose **Inspect runtime recipes** to see GPU placement, context, KV, Flash
Attention, CPU-MoE, external draft, EAGLE-3, and speculative parameter plans
for the selected runtime. `Unknown` means the
runtime has not supplied the exact evidence needed; it is not an invitation to
force the flag. Select an Available recipe, enter a controlled prompt, and use
**Run selected recipe**. Lab runs the baseline plus a small candidate set with
three fixed greedy repetitions. **Cancel recipe** stops at the owned request or
process boundary. Results and retained evidence refresh automatically after a
successful, failed, or cancelled recipe; the Evidence tab's **Refresh** button
is still available for an explicit reload.

The trade-off table reports the candidate name, decode speed and valid absolute
and percentage deltas, predicted and observed RAM/GPU, correctness, and any
exclusion reason together. Ties and Unknown measurements do not produce a
manufactured ranking. Buffered llama-server replies do not
provide trustworthy TTFT, so it remains Unknown. Low-bit KV results without a
referenced quality score cannot be applied. CPU-MoE may likewise show an
Unknown analytical total while retaining measured memory and throughput. Lab
never selects or applies the fastest row automatically.

External drafting requires a target and companion already selected in the
Services configuration and represented by verified model-manifest hashes. Lab
also checks tokenizer, vocabulary, model family, and EAGLE target-binding
metadata. Equal vocabulary sizes alone do not unlock the recipe. Parameter
sweeps require an explicit saved baseline for draft maximum/minimum,
probability, or draft GPU layers, because Lab does not assume runtime defaults.
Drafted and accepted counters appear only when the runtime reports them; zero
drafted remains zero while its acceptance ratio is undefined.

The **Prompt/shared-prefix timing effect** recipe sends the same three
reconstructed prompts with prompt caching disabled and enabled. The comparison
shows prompt milliseconds and throughput plus exact output correctness. It does
not show a reused-token estimate. `Reused tokens Unknown` means the selected
runtime has no proven direct counter schema, even if the cached side is faster.
Only prompt hashes, not the typed prefix, are stored in the experiment
definition.

Large runs are persisted as bounded immutable evidence slices linked from the
completion summary. All slices and that completion marker are committed as one
SQLite transaction, so a process death before commit leaves no authoritative
partial set. The slices remain the authoritative normalized evidence, while Lab
groups their durable run id into one top-level Evidence entry. Its drill-down
can still inspect, correct, remove, or export the individual records without
putting the whole run into one oversized experience document.

The configured Chat server is selected automatically when there is exactly one
non-embedding server. With multiple servers, choose the intended server before
freezing the definition. Lab recipe controls report their runtime gate and
remain visible when a capability is unavailable or Unknown.

Use **Lab > Evidence** to inspect structured Agent,
GPU Fit, and experiment evidence. Filters cover domain, project/workspace
scope, model/runtime fingerprints, normalized outcome, evidence origin, status,
and date. Select an execution entry to inspect its concise result summary, then
expand the retained records for canonical context, action, provenance, and raw
source links.

**Save correction** creates a linked replacement without rewriting the source
task or run. **Remove** asks for confirmation and permanently deletes the
selected empirical index record; a record with a dependent correction must be
handled from the dependent record first. Check one or more execution entries
and choose **Export selected** to prepare versioned redacted JSON for their
retained records in the detail pane.

## Voice

Native Kokoro runs locally after its verified assets are installed. Other voice
providers may require Python packages, local services, or an API key. Configure
the provider in Services or Settings, use Doctor for readiness, and check
Runtime Logs if synthesis fails. A missing, integrity, or load failure from
native Kokoro exposes **Open Doctor** directly in its Services status row
because Doctor owns the verified asset diagnosis and repair action. Remote
voice providers receive the text sent for speech. **Settings > Voice** lists the active provider's discovered names
for per-channel voice routing, while **Services > Voice** keeps the explicit
Save Config action for provider, device, speed, and process settings.

## Activity, logs, and troubleshooting

**Activity** records completed outcomes such as downloads, ingests, backups,
and managed-server events. **Runtime Logs** contain live operational detail,
apply redaction before display or persistence, and retain useful aggregate
timing while filtering repetitive low-level slot scheduler chatter. Diagnostic notifications that
offer **Copy details** copy only their detail text.

When something fails:

1. Open Doctor and run a fresh scan.
2. Inspect the failing check's details and any approval plan.
3. Check Services for the configured executable, model, port, and process log.
4. Check Runtime Logs for the first error, not only the final summary.
5. Confirm the model or service is local or remote before sharing sensitive
   context.

Do not edit SQLite files while Hermaeus is running. Do not repair missing AI
assets by copying unverified binaries over a managed installation.

## Settings, privacy, and backup

Default settings live at `%LOCALAPPDATA%\Hermaeus\settings.json` on Windows and
`~/.local/share/Hermaeus/settings.json` on Linux. Data Root may point elsewhere.
Secrets are stored through the configured secret store and settings retain
references, not raw keys.

Local models keep inference on the machine, but remote model, embedding, voice,
and integration providers receive the content sent to them. Attachments,
Knowledge excerpts, memory, and Recall context can all become part of a remote
prompt when their relevant features are enabled. Review the Local/Remote badge
and Privacy Audit before using sensitive material.

Use Settings' backup flow for Data Root. Back up AI Assets separately only if
avoiding large re-downloads matters; those files are replaceable, while Data
Root contains the user-created state that is not. Credentials and fallback
secret material are not included in Data Root backups. Re-enter credentials
after restoring on another machine.
