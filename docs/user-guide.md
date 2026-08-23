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
