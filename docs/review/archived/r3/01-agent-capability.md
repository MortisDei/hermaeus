# r3-01: Agent Capability - Closing the Gap to Claude Code / Codex

The bar is Claude Code and Codex. What makes those agents effective is
not secret sauce in the model prompt; it is architecture:

1. They **loop**: model acts, tool result comes back, model acts again,
   until the task is done or input is needed.
2. They keep a **transcript**: the model sees what it already read,
   tried, and learned this session, compacted when it grows.
3. They call tools **natively** (provider tool-calling APIs), often
   several per turn.
4. They edit **surgically** (string replace / diff hunks), never by
   rewriting whole files.
5. They **verify**: run the build/tests, read the failure, fix, re-run.

Aether's agent today has none of these five, by design of the original
read-first slice. All five can land without weakening a single safety
gate. The items below are ordered by leverage.

## A. Transcript-based steps (highest leverage)

Current behaviour: every step re-serialises an `AgentContextPack` into
one user message ([AgentService.cs:114-126](../../src/Aether.Agent/Services/AgentService.cs#L114-L126)).
The pack carries only the **last five** tool results
([AgentContextBuilder.cs:43](../../src/Aether.Agent/Services/AgentContextBuilder.cs#L43)),
each flattened to JSON and truncated at **4000 chars**
([AgentToolExecutor.cs:199-203](../../src/Aether.Agent/Services/AgentToolExecutor.cs#L199-L203)).
Net effect: the model reads a file, and two steps later has forgotten
most of it. No frontier CLI agent could work under this regime either.

Change:

- Persist a per-task step transcript (`transcript.jsonl` in the task
  directory, alongside `task_state.json`): the context pack once at
  task start, then alternating assistant responses and tool results.
- Each step's LLM call sends: system prompt + initial pack + transcript
  tail under a token budget (reuse `ContextPackBuilder` estimation;
  make the budget an `AgentSettings` field, default sized for local
  models, e.g. 12k tokens).
- When the tail exceeds budget, compact: fold evicted tool results into
  a rolling summary line in `task_state.json` (`Summary` already
  exists) and keep the most recent results verbatim. Deterministic
  compaction first; an optional LLM summarisation pass can come later.
- Tool results enter the transcript with a per-tool cap, not a blanket
  4000-char JSON slice: `read_file` deserves far more than
  `list_files`. Truncate content with an explicit
  `[truncated: N of M lines]` marker, never mid-JSON-structure.

Acceptance: an agent task that reads a 300-line file can quote a line
from it three steps later; transcript replays from disk after app
restart; `AgentContextPackStaysBounded` equivalent exists for the
transcript budget.

## B. Autonomous multi-step runs

Current behaviour: Start executes exactly one step
([AgentViewModel.cs:377](../../src/Aether.ViewModels/AgentViewModel.cs#L377));
the user clicks "Run step" for every subsequent LLM call. That is a
stepper, not an agent.

Change:

- `RunAsync(taskId, options, ct)` on `IAgentService`: loop
  `RunStepAsync` until the action type is `final` or `ask_user`, the
  gate returns `RequiresApproval` or `Blocked`, the step cap is hit
  (new `AgentSettings.MaxAutoSteps`, default ~20), or the token is
  cancelled.
- The loop pauses (never auto-approves) on any approval-gated action;
  after the user approves and the tool executes, the loop may resume.
  Optionally add "approve and continue" in the UI so one click both
  approves and resumes.
- UI: live step feed (thought summary + tool + result line per step),
  Stop already wired via CTS. Status strip shows `step 7/20`.
- Every loop iteration still writes the same per-step log/trace rows;
  nothing about auditability changes.

Acceptance: a goal like "find where X is configured and summarise it"
completes with zero clicks after Start on a workspace fixture; the
loop stops at the cap; Stop cancels mid-loop; an approval-gated action
pauses the loop and resumes after approval.

## C. Native tool calling, with the JSON protocol as fallback

`ILlmService` has no tool support at all (no tool types anywhere in
`Aether.Core`). The agent relies on "return only valid JSON" prompting
plus brace-matching extraction ([AgentService.cs:317-404](../../src/Aether.Agent/Services/AgentService.cs#L317-L404)).
llama.cpp's server, Ollama, and every OpenAI-compatible endpoint now
support the OpenAI `tools`/`tool_calls` wire format, and models
fine-tuned for tool calling are markedly more reliable through that
path than through free-form JSON.

Change:

- Add `LlmToolDefinition` (name, description, JSON-schema parameters)
  and `LlmToolCall` to `Aether.Core`; extend `LlmChatOptions` with
  `Tools`; extend the chat streaming contract so a response can end in
  tool calls instead of text (a typed stream event or a completion
  envelope, mirroring how `RagStreamEvent` replaced sentinel strings in
  r2).
- Implement in `OpenAiService`, `LlamaCppService`, `OllamaService`.
  Capability-detect per provider/model; when unsupported, fall back to
  the existing JSON protocol automatically (keep `ExtractJson`).
- The agent declares its tool set (workspace tools + `mcp:` bridged
  tools) as definitions instead of prose in the system prompt.
- Multiple tool calls in one response: execute all *gate-allowed
  read-only* calls in the same step and append each result to the
  transcript; if any call requires approval, queue them individually.

Acceptance: with a tool-calling model the agent runs a task end to end
with zero JSON parse failures in the trace; with a non-tool model the
JSON fallback path still passes the existing loop test; the safety gate
evaluates every call in a multi-call response independently.

## D. Surgical edit tools

`draft_patch`/`apply_draft_patch` accept only whole-file
`proposed_content` ([AgentToolExecutor.cs:60-69](../../src/Aether.Agent/Services/AgentToolExecutor.cs#L60-L69)).
Whole-file rewrite is the highest-failure-rate edit primitive there is
for local models (silent elisions, "rest of file unchanged" comments)
and burns tokens proportional to file size, not change size.

Change:

- New tool `edit_file(relative_path, old_string, new_string)`: exact,
  unique match required (fail with a count if 0 or >1 matches), same
  path containment as every other tool, approval-gated exactly like
  `apply_draft_patch`, `baseHash` stale protection retained.
- New tool `create_file(relative_path, content)` for new files
  (approval-gated; refuses to overwrite an existing file).
- Review UI renders the change as a unified diff via the existing
  `PatchDiffService` rather than a whole-file preview.
- Keep `draft_patch` for genuine full-file cases; steer the model to
  `edit_file` in the tool descriptions.

Acceptance: an edit to one function in a 500-line file round-trips
through queue/approve/apply touching only that hunk; a non-unique
`old_string` returns an actionable error instead of applying; stale
`baseHash` still blocks.

## E. Navigation tools worth using

Current retrieval is pre-stuffed: `PickSearchQuery` extracts the single
most frequent word of 4+ chars from the goal
([AgentContextBuilder.cs:191-204](../../src/Aether.Agent/Services/AgentContextBuilder.cs#L191-L204))
and dumps the matches into the pack. Claude Code and Codex instead give
the model good search tools and let it drive.

Change:

- `read_file` gains optional `offset`/`limit` (line-based) so large
  files can be paged instead of truncated.
- `glob_files(pattern)`: bounded glob over the safe file list.
- `search_files` gains regex support, N context lines, per-file and
  total result caps (all bounded; reuse the existing safe-walk).
- `list_files` gains an optional subdirectory argument and a depth cap
  so it can act as a tree view.
- Slim the pre-stuffed workspace section of the context pack once the
  model can search for itself (keep instructions, memory, and RAG
  sections).

All read-only, all `Allowed` by the existing gate, no policy change.

Acceptance: each new tool has a path-safety test mirroring
`AgentWorkspaceToolsEnforcePathSafety`; regex search respects the caps
on an adversarial fixture (many matches, huge lines).

## F. The verify loop (run_command growth)

`run_command` supports exactly five verbatim recipes
([WorkspaceCommandRecipes.cs](../../src/Aether.Agent/Services/WorkspaceCommandRecipes.cs)),
which was the right first slice. But an agent that cannot build the
thing it just edited cannot verify its own work, and verification is
half of what makes Claude Code trustworthy. r2 said to wait for
`run_command` usage telemetry; r3's mandate ("chase Claude Code and
Codex") supersedes waiting.

Change, keeping both existing invariants (workspace must declare the
command AND it must match a fixed template; approval always required):

- Replace the verbatim dictionary with **template families**, still
  hardcoded: `dotnet build [project]`, `dotnet test [project]`,
  `dotnet run --project <project>` excluded (long-running),
  `npm test`, `npm run <script>` where `<script>` must exist in the
  workspace's `package.json`, `cargo build`, `cargo test`,
  `pytest [path]`. Optional path arguments are validated with the
  existing containment rules (inside the workspace, no traversal, no
  symlink escape); anything else stays blocked.
- Full stdout/stderr tail (bounded, e.g. last 200 lines) goes into the
  transcript so the model can read the actual compiler error, plus exit
  code as structured data (this also feeds the lesson store, doc 02).
- Per-task "remember this approval": after the user approves
  `dotnet build` once in a task, re-runs of the *identical* command in
  the *same task* may auto-execute. First run always asks. Recorded in
  `ApprovalHistory` and trace like any approval. This is what makes the
  edit-build-fix loop tolerable without ever granting blanket trust.

Acceptance: gate tests cover template matching (valid project path
inside workspace allowed-with-approval; `../x` blocked; undeclared
family blocked); the remembered-approval flag never survives the task
or applies to a different command string; a deliberately broken fixture
project produces a transcript in which the model can see the compiler
error text.

## G. Explicit plan tool

`state_update.pending/completed` already exists but is freeform. Add a
`set_plan` tool (list of steps with `pending|in_progress|done` status)
that replaces the plan atomically, rendered as a checklist in the
workbench. Cheap, and it anchors long runs the way TodoWrite does in
Claude Code. Gate disposition: `Allowed` (it only mutates task state).

## Explicitly unchanged

- The safety gate and its deterministic classification. Capability
  comes from the loop, memory, and tools above, never from loosening
  dispositions. `push`, network, installs, history rewrites stay
  blocked.
- Approval-first for anything that writes or executes.
- `task_state.json` as source of truth; the transcript is an additive
  artifact in the same directory.
