# r4-01: Interaction and Failure Semantics

The r3 loop runs, but four of its feedback channels are broken or
missing. Each item below is independently landable; the order given is
the dependency order. File references are against `0.9.43-alpha`
(commit 107d5d5).

## A. User-reply channel for ask_user

**Gap.** `ask_user` sets the task to `WaitingForUser`
(`AgentService.cs:309-310`) and the workbench shows the question, but
there is no input for the answer. Grep confirms: no reply command, no
`user`-role transcript entry is ever appended anywhere. If the user
clicks Run Step, the model gets the identical context back and asks
again. This is the single biggest remaining interaction gap versus
Claude Code/Codex.

**Change.**

1. `IAgentService` gains
   `Task AppendUserReplyAsync(string taskId, string reply, CancellationToken ct = default)`.
   Implementation: load state; require `Status == WaitingForUser` and
   `PendingToolAction is null` (a reply is not an approval and must
   never stand in for one); append an `AgentTranscriptEntry` with role
   `"user"`, `ToolName = null`, `Content = reply.Trim()`, current step
   number; set `Status = Running`; save; append a log line.
2. `AgentContextBuilder.AddTranscriptHistoryAsync`
   (`AgentContextBuilder.cs:108-152`) currently maps roles to
   `transcript-tool` / `transcript-assistant` only. Add a
   `transcript-user` branch for role `"user"` so replies render in the
   pack with the same budget rules.
3. `AgentViewModel`: when the current task is `WaitingForUser` with no
   pending tool action, show a reply TextBox + Send command that calls
   `AppendUserReplyAsync` and then resumes the autonomous loop exactly
   the way `ApproveReviewAsync` does (`AgentViewModel.cs:507-527`);
   factor that resume block into a shared private method instead of a
   third copy.
4. Mention the user-reply role in the system prompt only if the model
   needs it; it should not, since replies arrive as ordinary transcript
   context.

**Acceptance.** A task that asks a question can be answered from the
workbench; the answer appears in the transcript file, in the next
context pack under `transcript-user`, and the loop resumes without a
manual Run Step. Replying is rejected (no state change) when a tool
approval is pending. Tests cover the service method's guard conditions
and the context builder's user-role rendering.

## B. Real failure semantics

**Gap.** `AgentTaskStatus.Failed` exists (`AgentModels.cs:12`) but is
never assigned; the only reader is the already-finished guard
(`AgentService.cs:178`). When the model call or JSON parse throws
mid-step, the task was already saved as `Running`
(`AgentService.cs:181-182`) and stays there forever; the exception
surfaces only as a transient UI error. r3's task-terminal lesson
source is unimplementable until terminal states actually happen.

**Change.** In `RunStepAsync`:

1. Add `int ConsecutiveStepErrors` to `AgentTaskState` (default 0,
   reset to 0 after any successfully parsed response).
2. Wrap the stream + parse section (`AgentService.cs:187-209`) so a
   `JsonException` from `ExtractJson` no longer propagates: build the
   same synthetic ask_user response the null-deserialize path already
   builds (`AgentService.cs:659-670`), increment
   `ConsecutiveStepErrors`, and continue through the normal
   state-save/transcript/trace path so the failed step is recorded, not
   lost.
3. If `ConsecutiveStepErrors >= 3`, set `Status = Failed`, add a
   blocker (`"model responses unparseable 3 times in a row"`), append a
   final transcript entry, save.
4. For transport/tool exceptions that must still propagate (LLM
   unreachable, tool executor throw at `AgentService.cs:284-288`):
   before rethrowing, set `Status = WaitingForUser` and save, so no
   exception path can leave a task stranded in `Running`.
5. In `RunAsync` (`AgentService.cs:405-428`): when the loop exits
   because `steps >= maxSteps` while still `Running`, append a log +
   transcript note ("step budget exhausted after N steps") and set
   `Status = WaitingForUser`, save. Today the loop just stops silently
   and the task looks active.

**Acceptance.** A fake LLM returning garbage three times produces a
`Failed` task with the blocker recorded and every bad step visible in
the transcript; returning garbage twice then valid JSON resets the
counter. A fake LLM that always requests a read-only tool hits
`MaxAutoSteps` and lands in `WaitingForUser` with the budget note. No
test path leaves a task in `Running` after `RunStepAsync` throws.

## C. Transcript completeness for approved tools, and dead code

**Gap 1.** The approve path in `AppendApprovalAsync`
(`AgentService.cs:439-457`) executes the pending tool and stores the
result in `ToolResults`, but never calls `AppendTranscriptEntryAsync`.
Only the last five `ToolResults` reach the pack
(`AgentContextBuilder.cs:54`), so the results of gated actions, the
most consequential ones, age out of the model's view while trivial
read-only results persist in the transcript.

**Change.** After a successful approved execution, append the same
`role: "tool"` transcript entry `RunStepAsync` appends
(`AgentService.cs:362-370`), using the current `StepCount`.

**Gap 2.** `ExecuteApprovedToolAsync` (`AgentService.cs:631-647`) has
zero callers outside the interface. It also bypasses lesson capture
and the transcript, so any future caller would silently reintroduce
both gaps.

**Change.** Delete it from `IAgentService` and `AgentService`, and
from any test fakes that stub it.

**Acceptance.** After approving a queued `edit_file`, the tool result
appears in the transcript file and in the next pack's
`TranscriptHistory`. `ExecuteApprovedToolAsync` no longer exists;
solution builds with zero warnings.

## D. Native tool-call fidelity

**Gap.** `BuildResponseFromToolCall` (`AgentService.cs:681-707`)
discards everything except the first tool call: any prose the model
streamed alongside it is thrown away and replaced with the synthetic
"Calling X." string, which is what then lands in the transcript as the
assistant's thought. Native-tool-calling models therefore produce
strictly worse transcripts than JSON-protocol models. Additional tool
calls in the same turn are silently dropped (the comment says they are
"re-offered next step", but nothing records that they were requested).

**Change.**

1. Pass the accumulated `raw` text into `BuildResponseFromToolCall`;
   if non-blank, use it (trimmed) as `ThoughtSummary` instead of
   "Calling X.".
2. When `nativeToolCalls.Count > 1`, append the names of the dropped
   calls to `ThoughtSummary` ("also requested: Y, Z; one action per
   step") so the model sees in the next step's transcript that they
   did not run.

**Acceptance.** A fake tool-calling LLM emitting prose plus two tool
calls yields a transcript entry containing the prose and the
dropped-call note, and only the first tool executes.

## E. Stale text and context hygiene

Small, but they mislead the model every step:

1. `AgentContextBuilder.cs:55-59`: `KnownRisks` still says "command
   execution, network access, commit, and push are blocked". Commands
   are now allowed via approved recipes. Reword to match the system
   prompt: writes and commands are approval-gated recipe commands;
   network, installs, commits, pushes remain blocked.
2. `AgentService.cs:471-478`: the doc comment on
   `RecordLessonEvidenceForToolAsync` claims stated `[LESSON:]`
   evidence is "intentionally out of scope"; it shipped
   (`AgentService.cs:211`, `RecordStatedLessonsAsync`). Fix the
   comment.
3. `PickSearchQuery` (`AgentContextBuilder.cs:287-300`): the one-word
   goal heuristic still runs on **every** step, spending pack budget on
   a keyword search the model can now do itself with `search_files` /
   `glob_files`. Change `AddWorkspaceContext` to run only when the
   transcript is empty (first step); later steps rely on navigation
   tools and transcript history.
4. `ApplyResponse` (`AgentService.cs:798-813`): `PendingSteps` only
   ever grows; a step reported completed stays listed as pending
   forever, so long tasks show the model a contradictory state. After
   merging, remove from `PendingSteps` any entry present in
   `CompletedSteps` (trimmed, ordinal-ignore-case).

**Acceptance.** Pack for step 2+ of a task contains no `workspace`
retrieved-file items; `KnownRisks` text matches actual policy; a step
that completes "Build plan" removes it from `PendingSteps`; comment
fixed. Existing tests updated where they asserted the old text.
