# 05. An agent a small model can drive

## Why

Doc 01 builds a constraint contract and points it at memory extraction.
This is the consumer that matters more.

The agent's planner protocol is a JSON schema written in prose. The system
prompt ends with "Return only valid JSON matching:" followed by a literal
template (`AgentService.cs:62-81`) describing `thought_summary`,
`current_step`, a `next_action` object with a four-value `type` enum, a
`state_update` object with four arrays, `user_message`, and an optional
`reservations` list. That template maps property for property onto
`AgentPlannerResponse` (`AgentModels.cs:591-606`), `AgentNextAction` and
`AgentStateUpdate` (`:608-611`).

Every step of every agent run asks the model to hit that shape from memory,
and then defends against it missing:

- `ParseResponse` (`AgentService.cs:1521`) calls `ExtractJson(raw)` first,
  because the model routinely wraps the object in prose or fences.
- If deserialization throws, `TryRepairActionType` (`:1538`) attempts one
  targeted repair. Its comment names the failure exactly: "Overwhelmingly the
  field is next_action.type carrying a TOOL NAME (`set_plan`) instead of one
  of the four action kinds, with tool_name left null. The strict enum
  rejected the whole response for it, the step was reported to the user as
  unparseable JSON (which it was not), and the run stalled."
- If the repair does not apply, the step becomes a parse failure: error
  counters increment (`:311-312`), the action becomes `AskUser`, and the
  user gets "The agent could not parse the model's response."
- `DescribeParseFailure` (`:1691`) then classifies the wreckage, and its
  prose branch (`:1701-1703`) says the quiet part out loud:

  > "The model replied in prose instead of the JSON action format it was
  > asked for. **Smaller local models do this**; a model with stronger
  > instruction following, or one that supports native tool calling, avoids
  > it."

That is the app telling its own user that the local-first path works worse,
and recommending a bigger model. For an application whose first principle is
local-first, that is the single most consequential place where asking
politely instead of constraining costs something real.

Native tool calling already exists as a partial escape (`:293-305`): a
provider that supports it returns structured `tool_calls` and skips the text
protocol entirely. But `FixedToolDefinitions` (`:85-92`) declares only the
fixed workspace tool set, and its own comment records the limit: "MCP-bridged
(`mcp:`) tools are not declared natively; a model reaches those only through
the JSON `next_action` protocol described in the system prompt". So the text
protocol is not a legacy path for weak models. It is the only path to every
MCP tool, for every model.

Constraining it fixes both cases at once.

## The safety argument, stated up front

**Constraining the shape does not loosen the gate, and this document must
not be implemented in a way that does.**

The gate's inputs are a tool name and a mutation flag
(`AgentSafetyGate.Evaluate`), and the code already treats everything the
model asserts about its own risk as untrusted: `requires_approval` and
`risk_level` are fields in the prompt template (`AgentService.cs:69-70`) and
the dispatch path overrides them in code. `AgentService.cs:363-370` blocks
`plan_subtasks` on a sub-task "regardless of what the model set in
requires_approval", and `AgentSafetyGate.cs:41-45` forces approval for
`plan_subtasks` on the same principle.

A schema makes the model's answer *parseable*. It does not make it
*trusted*. After this document, a constrained response still goes through
the identical classification, and a model that emits
`"requires_approval": false` for `delete_file` is still blocked, exactly as
today.

The test that pins this is 5.5 and it is not optional.

## Work items

### 5.1 The planner protocol gets a real schema

Add a JSON schema for `AgentPlannerResponse` as a `const string` beside the
prompt template it duplicates.

There is already a precedent in this exact file:
`BuildFixedToolDefinitions` (`AgentService.cs:92`) builds native tool
declarations from hand-written JSON schema strings via a local
`Schema(string json)` helper. Follow that pattern. Do not add a schema
generation package and do not reflect over the types at runtime.

The schema must constrain, at minimum:

- `next_action.type` to the four action kinds as a JSON `enum`. This alone
  removes the failure `TryRepairActionType` was written for.
- `next_action.risk_level` to its four values as an `enum`.
- the required top-level properties, so a truncated object fails at the
  sampler rather than at the deserializer.
- `state_update`'s four properties as arrays of strings.

Tests: the schema and `AgentPlannerResponse` agree property for property
(the test that fails when someone adds a field to either); the schema's
enums match the C# enums by name; a document valid against the schema
deserializes without repair.

### 5.2 The planner call sends the constraint

The planner's `LlmChatOptions` gains doc 01's `OutputConstraint` set to
5.1's schema, when the selected agent model reports
`SupportsOutputConstraints` (doc 01 1.4).

Order of precedence is unchanged and matters: if the provider supports
native tool calling and returns `tool_calls`, that path still wins
(`AgentService.cs:303-304`). The constraint applies to the text protocol,
which is what runs when native tool calls are absent, and which is the only
path for MCP tools regardless.

### 5.3 Every fallback stays

`ExtractJson`, `TryRepairActionType`, the `JsonException` handler, the error
counters and `DescribeParseFailure` all remain exactly as they are.

They run for unconstrained providers, for remote endpoints that decline the
constraint, and for any case where the constraint reaches the server and the
output still does not deserialize. Deleting a fallback because the happy
path improved on one machine is how this round would break every
configuration it was not tested against, and the failure mode here is an
agent run that stalls with an unhelpful message.

What should change is the *frequency*, and 5.6 makes that observable.

### 5.4 `DescribeParseFailure` stops recommending a bigger model

The prose branch at `:1701-1703` currently tells the user their model is too
small and suggests one with stronger instruction following. After 5.2, that
advice is wrong in a specific way: on a local llama.cpp model the honest
message is that the response was not constrained, and constraining it is
something the app can do rather than something the user should solve by
downloading 20 GB.

Rewrite that branch to distinguish the cases:

- constraint was applied and output still failed: keep a version of the
  current message, because now the model genuinely is the problem.
- constraint was available and not applied: say so, and name where it is
  turned on.
- provider cannot constrain: the current message, which is accurate there.

Follow `docs/mascot.md` "Voice in UI copy". When in doubt, drop the
personality and state the fact.

### 5.5 The gate is provably unchanged

A regression test that constructs a constrained planner response asserting
`"requires_approval": false` and `"risk_level": "none"` for a high-risk tool
(`delete_file`, `run_command`, `install_package`), runs it through the real
dispatch path, and asserts the outcome is identical to the same response
arriving unconstrained: blocked, with the gate's own reason.

This test exists to fail loudly if a future change ever lets a schema-valid
response bypass classification. Name it for that behaviour, not for this
round.

### 5.6 Parse failures are countable

`AgentTaskState` already tracks `ConsecutiveStepErrors` and
`TotalStepErrors` (`AgentService.cs:311-312`). Record alongside the task
whether the run's planner calls were constrained.

One boolean per run, visible in the task's own trace. It is the only way to
answer "did this help" without running a benchmark, and it is the honest
form of that question: a count of parse failures with and without the
constraint, on real runs, rather than a claim in a changelog.

Not a dashboard, not a metric panel, not a comparison view. A field on the
record and a line in the existing trace.

## Deliberately out of scope

**Declaring MCP tools natively.** `FixedToolDefinitions`'s comment explains
why they are not: an MCP server's tool list is dynamic and arrives at
runtime. Constraining the text protocol is the fix that covers them; turning
them into native declarations is a separate design question about trusting a
server's self-description, and r26 already rejected widening what a server
claims about itself into a safety decision.

**Changing the four action kinds, the prompt template's wording, or the
planner protocol's shape.** 5.1 describes the existing shape in a schema. A
round that constrains a protocol should not also redesign it.

**Removing `TryRepairActionType` once the enum is constrained.** It still
runs on every unconstrained path. See 5.3.

**Loosening any risk level, or letting a constrained response carry its own
approval decision.** See the safety argument above and 5.5.

**A retry loop that re-prompts on parse failure.** The error budget and
`AskUser` fallback are the existing behaviour and they are deliberate. A
round that adds automatic retries is changing how much work runs without
approval, which is the thing `plan_subtasks` is gated for.
