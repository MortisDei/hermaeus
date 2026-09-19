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
autosave never performs that migration implicitly. The Data Storage page shows
both the configured root and the root currently effective for composed stores.
Restart Hermaeus after a successful change so every store and cached view is
composed against the selected root. Startup migration verifies each copied file
before committing the destination; a failed attempt keeps the old root active,
leaves the pending destination available for retry, and records the outcome in
Data Storage. After scheduling a move, a second dialog offers **Restart now**
or **Restart later**. Restart later leaves the current effective root active;
Restart now performs the controlled application restart required for bootstrap
migration. No active files move before that bootstrap step.

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
the effective tuned GPU layers, threads, and context directly as separate
wrappable metadata fields. Open the card's
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

Switching the Services model picker starts a new model from that model's card
and tune profile, or from safe defaults when no profile exists. Unsaved runtime
fields are kept separately per model and restored when you switch back; a draft
model, projector, context, placement, cache, speculative, or adaptive setting
from another model is never inherited.

Services auto-tune uses the saved server configuration as its probe baseline and
changes only the candidate axes for each transient probe. Models-card
auto-tune instead builds an isolated target probe from the selected model and
only its verified companions, so it does not inherit another loaded Chat
model's draft, projector, or extra arguments. Before a Models-card tune,
currently running managed servers are stopped at the awaited process boundary;
the previously running servers are restored after success, failure, or
cancellation. A target that is itself running must still be stopped first.
When available, local GGUF metadata and the current hardware profile inform the
probe envelope. Tuning does not silently overwrite the saved runtime
configuration; a model profile is saved only after the prior running servers
have been restored. Services and Models expose cancellation for an individual
tune and the **Auto-tune all** operation. Cancellation reports a cancelled
result, releases the probe, restores the source services, and does not save a
profile after the cancellation boundary.

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
asset is available or the selected build fails executable or build-identity
validation, Hermaeus
refuses the update with an explanation rather than silently replacing it with a
CPU build. CPU is still available when selected explicitly. Fresh managed
archives are stored under one Hermaeus build directory; older nested archive
layouts remain discoverable for repair and pruning.

The Data Storage panel shows the configured request and the last installed
backend separately. The latter is installation history, not a replacement for
Auto and not proof of which executable a currently running process uses. The
active process identity remains tied to the Services executable and Chat
runtime telemetry. Persisted capability cache and sibling state resolve beneath
the effective Data Root. A root change is write-probed before settings are
published, and a capability-cache write failure is shown as a failure with its
path rather than as successful persistence.

For a GPU update, Hermaeus runs the downloaded executable with `--version` and
captures both output streams. Current llama.cpp Windows builds write their
version/build identity to stderr. The verified upstream release tag and
SHA256-checked archive remain the stable identity evidence when a runtime's
version text cannot be parsed. `--help`, a help-shaped output, or exit code 0
alone is never accepted as identity proof.

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

The model card's companion list also has a **Clear** action for a stale or unwanted
mapping. After confirmation it removes only that companion file under the Local AI
assets root, when the file is present, and removes only that mapping from the
primary model's manifest. It does not delete the primary model or unrelated
companions.

When the model card declares a Hugging Face thumbnail, the selected repository
and its download cards may show it as optional repository artwork. Hermaeus
reads only the bounded `cardData.thumbnail` value, requires an exact immutable
repository revision, blocks arbitrary external hosts, and checks MIME, magic,
dimensions, and decoded size before loading it. If the repository does not
declare artwork, Hermaeus may use the publisher or organization avatar from
the exact Hugging Face avatar host as a clearly labelled fallback. It is not
treated as repository-declared artwork. Missing, invalid, unavailable, or
unsafe artwork falls back to the generic mark. Artwork does not affect model
identity, fit, trust, ranking, or download selection.
The cache is rebuildable and excluded from Data Root backups. Settings >
Data Storage shows its size and provides a confirmed Clear action that does
not remove models or manifests. A custom model avatar remains separate and
takes precedence over cached repository artwork.

When a repository is selected, known GGUF variants appear immediately while
fit and companion checks complete independently per row. A row remains
download-disabled while its compatibility check is still running.
Selecting another repository or leaving Models cancels the superseded inspection
without publishing stale artwork or details; an expected cancellation is not
shown as a command failure.

The server card's **GPU Fit** text is a prediction for the values currently in
the editor, including unsaved changes. It lists weights, K/V cache, runtime
overhead, companions, and headroom separately. `Unknown` means a material input
or trustworthy measurement is missing; it is not treated as zero. Runtime
observations remain separate and are comparable only under the exact v2
runtime/model/hardware/configuration fingerprint.

**System Overview** also shows a whole-workload resource snapshot. It lists
registered consumers and their active allocations, whole-device memory totals,
and evidence states that prevent false precision. Active values identify
observed or planned bytes; a component attribution gap, a non-resident
consumer, and a lazy consumer are shown separately. Each managed server's
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
Runtime Logs. Ready informational checks do not expose a repair or navigation
action; non-ready checks expose only the action relevant to that check.

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

The compact **Quick Chat** surface sends with Enter or Ctrl+Enter; use
Shift+Enter for a newline. It shows a Processing indicator while the request is
active.

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

**Knowledge** ingests files into local RAG datasets. In the RAG panel, select
one or more datasets for each question from **Datasets included in this
question**. The first time the panel is used, none is selected and asking
prompts for an explicit choice. The last choice is retained for later
questions. If no dataset exists, **Create Hermaeus Help dataset** offers to
ingest the version-local help documents shipped with this build through the
normal pipeline, with ordinary citations and provenance. The normal folder
ingest path remains available. Attach one dataset to a Chat conversation from the Knowledge
picker. Retrieval is bounded and cited; weak
matches are omitted instead of forced into every answer. Reindex after changing
the embedding model. Dataset Manager shows the published generation history,
while ordinary retrieval uses only the current complete generation. A cancelled
or failed ingest leaves the prior generation in place. Removing missing sources
requires separate confirmation and publishes a replacement generation rather
than deleting live rows mid-ingest.

The RAG workspace separates **Ask**, **Manage**, **Sources**, and
**Diagnostics**. Use **Manage** for persistent dataset administration,
ingestion, watched folders, reindexing, and deletion. The dataset scope in
**Ask** remains question-specific and does not change the selected dataset used
by management actions.

**Memories** are durable, reviewable facts stored under Data Root. Settings
control whether memory and Recall context may be injected into Chat. The Chat
environment description reports only enabled context sources. The command
palette can search the local Recall index even when Recall injection into Chat
is disabled. The Memories view puts pinned memories in a clearly labelled
section at the top, where **Unpin** is available directly. Agent workspace
notes and generated workspace profiles are shown in Agent, not mixed into
ordinary Memories. Weak semantic memory candidates are left out of ordinary
recall. Pinning affects prominence only after relevance, so a pinned but
unrelated memory is not injected. The RAG ingest plan is analysis context and
is not saved as a normal memory.

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

In RAG, the Ask view makes the current knowledge state explicit. It says when
the question is ready, searching/generating, answered, refused because
retrieval was not trustworthy, failed, cancelled, or blocked because no
knowledge base exists. The next-action line points to dataset selection,
retry, evidence inspection, or dataset creation. Dataset scope labels identify
Local files and Remote web sources. Sources and Diagnostics are secondary
views for citation and trace inspection, while the normal answer remains
compact. Chat's Knowledge attachment continues to use the existing bounded
retrieval and citation path.

## Agent workspaces

The **Agent** is separate from normal Chat. Select a workspace root, review its
scope and proposed actions, and approve gated operations explicitly. Task state
and patch queues remain inspectable. Workspace authority does not carry into
another workspace or into ordinary Chat.

The Agent workbench keeps the job flow in **Run**, **Changes**, **Workspace**,
and **History**. A decision strip remains visible above the tabs when the Agent
needs an answer or approval. Run shows plain-language lifecycle state and next
action. When a run ends, **See the changes** opens the verified file ledger and
**Open run artifacts** opens the persisted task state, transcript, trace, and
log folder when it exists. **New task** clears the current composer, response,
draft, selected file, and task-scoped evidence without deleting the old task.
Workspace editing uses AvaloniaEdit as the sole local editor. Save or Ctrl+S
writes the selected file directly as an owner action after comparing the
revision hash that was loaded; the write uses atomic replacement and reports a
conflict with a Reload action message when the file changed outside Hermaeus.
File listings distinguish directories and show **Modified time unavailable**
when the filesystem cannot provide a trustworthy timestamp. Equivalent Agent
mutations are bounded as non-progress when the requested action or verified
post-image is repeated, while legitimate iterative edits remain reviewable.
Agent-proposed patches remain separate and use the normal prepared mutation and
approval path. In Changes, each prepared patch's Approve, Reject, and Block
controls act on that exact authoritative proposal and refuse stale revisions
or content instead of silently deciding a newer patch. The editor status tooltip exposes bounded attachment,
dimensions, visibility, editable state, document-size, and file diagnostics for
native troubleshooting.
Starting another top-level task is disabled while one is open. An
orchestration parent cannot be finished or dismissed while a child is pending
or running. If startup finds that inconsistent state, it marks the parent
blocked so **Continue** can reconcile it. When a workspace is missing its
`AGENTS.md`, **Review and create AGENTS.md** previews the file and places a
normal prepared mutation in the approval queue; it does not write the file
directly.

If no Agent task is open, the preview must be accepted before Hermaeus creates
an explicit `Create workspace AGENTS.md` task and queues the patch. Existing
`AGENTS.md` content is never overwritten by this action.

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
event. The same trace line identifies the selected provider tag and reports
reasoning-event/character counts when the provider emits a reasoning stream,
so reasoning time is not mistaken for answer content latency.
The telemetry identity line uses the runtime kind/version/build/backend and a
manifest or local model label with architecture and quantization. Stable
identity hashes remain in the tooltip for diagnostics.

When process VRAM is unavailable, the flyout keeps the value as `Unknown` and
shows the bounded evidence reason. It does not substitute whole-device usage
or zero for a missing process counter.

Settings > Voice contains supplementary audio feedback controls for the
explicit task/runtime/recording event list. Volume is retained when muted,
visual notifications remain authoritative, and cues are suppressed while TTS
speaks by default. A suppressed cue waits for TTS to finish and then rechecks
settings before playback. Playback failure does not fail the operation that
raised the visual notification. Each event uses its own bounded generated cue
pattern rather than one generic beep, and the trace records the cue identity,
backend attempts, fallback reason, and result.

When Recall injection is enabled, the Chat trace identifies keyword-only
fallback retrieval separately from embedding-backed retrieval. Lexical hits
remain usable, but their presence does not claim that semantic retrieval is
healthy. Runtime logs identify source hit counts, relevance survivors,
context-budget selection, and the number actually injected. Optional embedding
backfill yields to interactive query embedding; missing server timing headers
remain explicitly unavailable rather than being inferred.

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

**Inspect runtime recipes** explains whether each recipe is Available,
Unavailable, or Unknown for the selected model and runtime. Unavailable recipes
cannot be run, and no configured server disables the start/inspection actions
with an explanation of what to configure. The run card keeps execution,
cancellation, source restore, recommendation, and effective runtime state
separate. Recipe prompt detail and evidence filters are tucked behind secondary
disclosures so the next action stays visible on a narrow window.

While a recipe is running, the Experiment card names the experiment and current
candidate, shows its position and stage, and reports completed and remaining
workload steps with a determinate percentage. Its terminal state carries the
actual result instead of leaving a generic running indicator.

The run state names isolation and comparison refusals. Missing counters remain
missing. A comparison cannot show a headline delta when runtime, model,
hardware, or configuration fingerprints differ, and a deterministic output
difference fails correctness regardless of speed.
The isolated runtime also reports effective context and slots from its
structured properties endpoint, including the nested shape used by current
b10930 runtimes. GPU placement comes from the owned process's startup receipt,
which explicitly reports the offloaded layer count. Each effective receipt is
associated with the runtime PID, redacted exact argv, executable path, and
bounded startup evidence. Those values must be auditable and match the
reviewed baseline or candidate before that comparison is controlled; Auto
placement additionally requires effective fit evidence. If effective evidence
is missing, lacks its process association, or mismatches, the run is
**Inconclusive**, not successful, and Apply remains unavailable. After
confirmation, Lab reads the live Services
projection back after saving so a successful Apply means the reviewed fields
are visible in settings, not merely that a save call returned.

Every shipped recipe also requires proof of the field it changes. KV recipes
require effective K/V cache types, Flash Attention requires its effective mode,
CPU-MoE requires effective CPU expert placement, speculative recipes require
the effective mechanism and relevant parameter, and prompt-prefix reuse
requires a prompt-cache observation. If the runtime cannot expose that field,
the workload may finish but remains Inconclusive. Lab and Benchmarks use the
same runtime evidence envelope, so requested settings or configuration
fingerprints cannot stand in for effective runtime or telemetry identity.

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
The execution list leads with a human result summary for failed, cancelled, or
inconclusive runs. Technical ids, normalized outcomes, and raw evidence are
available under a separate technical-evidence disclosure.
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
voice providers receive the text sent for speech. **Settings > Voice** lists the
active provider's discovered names in editable, unfiltered per-channel
selectors. Reopening a selector does not reuse the previous voice as a filter,
and a provider without a catalogue still permits a manually entered voice id.
**Services > Voice** keeps the explicit Save Config action for provider, device,
speed, and process settings.

## Activity, logs, and troubleshooting

Background **Activity** is available as a collapsed section inside **Memories**
and records completed outcomes such as downloads, ingests, backups,
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
secret material are not included in Data Root backups. Restore preflights all
file entries for safe paths, duplicate targets, and size budgets of 10,000 file
entries, 128 MiB per entry, and 512 MiB total by default. The archive is first
expanded beneath a temporary staging root, with actual expanded bytes checked
while copying. Commit moves are rollback-safe and existing files still require
the explicit overwrite choice. Re-enter credentials after restoring on another
machine.

### Optional ChatGPT Pet companion

Settings > Interface > Companion can import a generic ChatGPT Pet v2 package.
Hermaeus accepts only bounded data files with a valid manifest, supported sprite
version, safe relative paths, and no scripts or reparse points. The bundled
Moss package is available as a choice, but the overlay is off by default. When
enabled it is a small draggable animated overlay; its package selection and
position are saved through normal UI settings and the whole sprite is clamped
inside the window. The package never receives chat content or filesystem
authority. Moss provenance is recorded beside the asset, and its external
redistribution/licensing status still needs owner confirmation.
