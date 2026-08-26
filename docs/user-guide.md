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
live database files by hand.

Choose a chat backend next. For managed llama.cpp, use **Install managed
llama.cpp** before reaching Doctor. You can then choose an existing GGUF or
download one of the small, SHA256-verified starter models. Voice is optional;
native Kokoro can be installed during onboarding or later through Doctor.

## Models and Services

**Models** lists GGUF files and models reported by configured providers. A
Hugging Face source badge means Hermaeus retained download provenance. Open a
model card to edit its display name and per-model defaults or to inspect its
source.

**Services** owns processes and files on disk. A managed llama.cpp server needs
the resolved `llama-server` executable, a GGUF model, a localhost port, and
launch settings. Start it there, then select its model in Chat. Runtime Logs
show the exact startup stage and sanitized process output.

Capability status is evidence-scoped. `Available` means the selected runtime,
and the selected model where relevant, advertised or demonstrated the feature.
`Unavailable` requires a successful authoritative probe. A failed probe is
`Unknown`. In particular, finding NextN metadata and a generic `draft-mtp` flag
does not prove that MTP engages for that model.

The server card's **GPU Fit** text is a prediction for the values currently in
the editor, including unsaved changes. It lists weights, K/V cache, runtime
overhead, companions, and headroom separately. `Unknown` means a material input
or trustworthy measurement is missing; it is not treated as zero. Runtime
observations remain separate and are comparable only under the exact v2
runtime/model/hardware/configuration fingerprint.

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
build and links a successful installation back to Services.

## Chat and context

Choose a model at the top of **Chat**, type a message, and send. Stop cancels an
active generation. Regenerate creates a branch rather than destroying the
previous answer. Deleting the active conversation returns Chat to a fresh,
focused input.

Attach text, code, PDF, DOCX, or supported image files from the attachment
control, drag and drop, or clipboard. Images are sent only when the selected
route actually accepts them. The **Context Inspector** shows the environment
context, prompt, draft, history estimate, attachments, and attached Knowledge
context. Normal Chat does not expose web access, a shell, tool calls, or Agent
workspace actions.

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
the embedding model.

**Memories** are durable, reviewable facts stored under Data Root. Settings
control whether memory and Recall context may be injected into Chat. The Chat
environment description reports only enabled context sources. The command
palette can search the local Recall index even when Recall injection into Chat
is disabled.

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

Settings > Voice contains supplementary audio feedback controls for the
explicit task/runtime/recording event list. Volume is retained when muted,
visual notifications remain authoritative, and cues are suppressed while TTS
speaks by default. Playback failure does not fail the operation that raised the
visual notification.

## Lab experiments and evidence

Open **Lab > Experiment**, select a configured Chat server, name the run, and
set the bounded candidate values shown by the editor. **Freeze and start**
captures the exact definition and starts a separate runtime on a temporary
loopback port. The Services configuration and active Chat server remain
unchanged. **Complete baseline** preserves the shell observation and cleans up
the temporary process; **Cancel** stops only the runtime owned by that run and
records the cancelled or partial result.

The run state names isolation and comparison refusals. Missing counters remain
missing. A comparison cannot show a headline delta when runtime, model,
hardware, or configuration fingerprints differ, and a deterministic output
difference fails correctness regardless of speed.

**Review candidate** lists the exact Services fields that would change. A
speed-only, uncontrolled, missing-correctness, or stale result is refused.
**Confirm reviewed changes** asks once more, rechecks the selected server plus
runtime/model identity, and saves through the normal Settings path. Review is
separate from running an experiment, and experiment evidence is retained.

Choose **Inspect runtime recipes** to see GPU placement, context, KV, Flash
Attention, CPU-MoE, external draft, EAGLE-3, and speculative parameter plans
for the selected runtime. `Unknown` means the
runtime has not supplied the exact evidence needed; it is not an invitation to
force the flag. Select an Available recipe, enter a controlled prompt, and use
**Run selected recipe**. Lab runs the baseline plus a small candidate set with
three fixed greedy repetitions. **Cancel recipe** stops at the owned request or
process boundary.

The trade-off table reports decode speed, predicted and observed RAM/GPU,
correctness, and any refusal together. Buffered llama-server replies do not
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

Use **Lab > Evidence** to inspect structured Agent,
GPU Fit, and experiment evidence. Filters cover domain, project/workspace
scope, model/runtime fingerprints, normalized outcome, evidence origin, status,
and date. Select a record to inspect its canonical context and action plus the
raw-source links.

**Save correction** creates a linked replacement without rewriting the source
task or run. **Remove** asks for confirmation and permanently deletes only the
selected empirical index record; a record with a dependent correction must be
handled from the dependent record first. Check one or more rows and choose
**Export selected** to prepare versioned redacted JSON in the detail pane.

## Voice

Native Kokoro runs locally after its verified assets are installed. Other voice
providers may require Python packages, local services, or an API key. Configure
the provider in Services or Settings, use Doctor for readiness, and check
Runtime Logs if synthesis fails. Remote voice providers receive the text sent
for speech.

## Activity, logs, and troubleshooting

**Activity** records completed outcomes such as downloads, ingests, backups,
and managed-server events. **Runtime Logs** contain live operational detail and
apply redaction before display or persistence. Diagnostic notifications that
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
