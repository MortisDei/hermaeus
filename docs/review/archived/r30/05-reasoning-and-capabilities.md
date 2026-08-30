# 05. Capability detection and preserved reasoning

## Outcome

r30 makes reasoning a first-class message channel. A local reasoning model can
stream thoughts separately from its answer, retain them across save and reload,
send them back on later turns when the template supports preserved reasoning,
and render them in a labelled collapsed section. The answer remains the answer:
copy, speech, memories, Recall indexing, and ordinary transcript search do not
silently consume private reasoning text.

This is one vertical feature. Do not ship the checkbox without the message
model, or the message model without history replay and a visible transcript.

## 5.1 One capability result with evidence

Add a small `LocalModelCapabilityService` in Services and immutable capability
records in Core. Each capability is `Available`, `Unavailable`, or `Unknown`
and carries a short evidence code plus detail. The service combines facts; it
does not infer from marketing filenames.

Offline GGUF facts:

- extend the bounded `GgufMetadataReader` with `NextnPredictLayers` from the
  architecture-suffixed `.nextn_predict_layers` scalar;
- record whether `tokenizer.chat_template` exists, but do not implement a second
  Jinja parser or claim template capabilities by substring;
- keep the parser's current size, nesting, and malformed-input limits. Reading
  one scalar and the presence of one bounded string must not materialize token
  arrays or tensor data;
- `NextnPredictLayers > 0` is positive embedded-MTP evidence. Missing or zero is
  Unknown unless a stable GGUF fact proves absence. Architecture alone is never
  positive evidence.

Runtime facts:

- cache `llama-server --help` feature flags by resolved executable path, size,
  and modification time. Launch it through `ProcessStartInfo.ArgumentList`;
- detect support for `--spec-type` with `draft-mtp`, `--reasoning-format`,
  `--reasoning`, and `--reasoning-preserve` independently;
- when a managed llama-server is healthy, parse `/props.chat_template_caps` and
  `/props.modalities`. `supports_preserve_reasoning=true` is the authoritative
  positive template result;
- cache the live result by model path, model size/mtime, executable path, and
  executable size/mtime using an atomic state-file write. A changed model or
  executable invalidates it. Do not put this derived cache in settings;
- an unreachable server leaves template capabilities Unknown. It does not erase
  the last matching file-identity result and never becomes Unavailable merely
  because the process is stopped.

The effective results are intersections. Embedded MTP is Available only when
the GGUF has NextN layers and the runtime supports `draft-mtp`. Reasoning
extraction is Available when the runtime supports a separate reasoning format;
template preservation is Available only when the matching `/props` result says
so. Vision reports the server modality when running and separately reports any
validated external projector candidate.

Models and Services show the result and evidence. Doctor reports Unknown or a
stale probe as information, not failure. No new architecture table belongs in
Doctor, Setup, or a ViewModel.

Acceptance criteria:

- a synthetic GGUF with `qwen35.nextn_predict_layers=1` plus a compatible help
  fixture reports embedded MTP Available;
- the same GGUF with an older executable reports it Unavailable at runtime and
  explains why;
- a missing key, stopped unprobed server, malformed `/props`, and changed file
  identity each produce the documented non-stale result;
- no test depends on a model filename, real owner data, or a downloaded model;
- capability cache writes are atomic and corrupt cache data is recoverable.

## 5.2 Reasoning is a separate transport channel

Extend the provider-neutral contracts additively:

- `ChatMessage.ReasoningContent`, nullable on the wire and empty for ordinary
  messages;
- `LlmStreamEvent.ReasoningDelta`, independent from `ContentDelta`;
- `ChatSendOrchestrator.StreamAsync` receives an `onReasoning` callback. A
  reasoning delta counts as the first stream event but not the first answer
  token, preserving the current timing distinction;
- `Message.ReasoningContent` and observable
  `MessageViewModel.ReasoningContent`, plus `HasReasoning` and expansion state.
  Expansion state is presentation-only and is not persisted.

Provider parsing:

- llama.cpp and OpenAI-compatible streams read
  `choices[0].delta.reasoning_content`; final usage-only chunks still survive;
- Ollama reads `message.thinking` when present;
- missing fields remain empty. Do not scrape `<think>` tags in Core or mutate
  old inline transcripts. The server parser owns model-specific syntax;
- request llama.cpp's separate `deepseek` reasoning format through its supported
  request field. If the selected runtime lacks that feature, omit the field and
  leave legacy inline output unchanged;
- provider errors, tool-call deltas, answer deltas, and reasoning deltas may
  share a chunk. Parsing one must not discard the others.

Keep the provider boundary explicit. Extend `ProviderCapabilities` with
separate reasoning-output and reasoning-history-input flags only where the
transport contract is known. Parsing an optional output field is harmless and
may be opportunistic; replaying stored reasoning is not. In r30, only the
managed llama.cpp route gains proven history input through its model/runtime
capability result. Ollama and generic OpenAI-compatible output may be captured,
but their stored reasoning is not replayed without an equally explicit future
input contract. Pass an `includeReasoning` policy into
`OpenAiCompatibleToolWire.BuildMessages`; do not make its shared shape send the
field to every compatible-looking endpoint.

The local API forwards the new channel rather than flattening it. Its accepted
assistant message shape reads optional `reasoning_content`; streaming responses
emit `delta.reasoning_content`; non-streaming responses emit
`message.reasoning_content`. Existing clients that only read `content` remain
compatible. Traces and the text-only convenience stream remain answer-only.

The chat path accumulates reasoning and answer independently, batches answer
rendering as it does now, and persists partial non-empty output on cancellation.
A reasoning-only response is not deleted as an empty assistant turn. It ends
with a visible `No final answer was returned` state and remains a benchmark
failure unless the benchmark case explicitly evaluates reasoning.

Acceptance criteria:

- interleaved reasoning/content fixtures preserve exact order inside each
  channel without leaking reasoning into `Content`;
- reasoning-only, content-only, usage-only, malformed, cancelled, tool-call,
  and error streams retain current semantics;
- first-event time is set by reasoning and first-token time is set only by
  answer content;
- TTS receives answer content only, including during streaming speech.
- local API request, streaming response, and non-streaming response fixtures
  preserve the separate field without changing content-only clients.

## 5.3 Persist, branch, truncate, and replay it

Conversation JSON already stores `Message` objects inside `messages_json`, so
`ReasoningContent` is an additive property and requires no SQLite migration.
Old rows deserialize empty. Save it through every mapping between `Message` and
`MessageViewModel`, including reload, active-path reconstruction, edit forks,
regenerate forks, continue, cancellation, and export snapshots.

History construction includes an assistant message's `reasoning_content` only
when all of these are true:

1. the saved message has non-empty reasoning;
2. the selected provider accepts the field;
3. the matching local template capability is
   `supports_preserve_reasoning=true`;
4. that managed server's `PreserveReasoning` setting is enabled.

Otherwise preserve the text on disk but omit it from the request. Never merge
it into ordinary content as a fallback. `OpenAiCompatibleToolWire` emits the
field beside assistant content and tool calls only when its caller enables the
policy, matching llama.cpp's accepted history shape.

Context-window truncation budgets answer plus preserved reasoning when it will
be sent. The Context Inspector labels the reasoning contribution separately.
When preservation is off or unsupported, its token estimate is zero because it
will not be on the wire.

Reasoning is excluded by default from conversation FTS, Recall indexing,
memory extraction, title generation, RAG ingestion, clipboard Copy, and TTS.
Those systems continue to use `Content`. `ConversationStore` currently places
the whole `messages_json` blob into FTS and searches that blob in its LIKE
fallback, so adding the property without changing the projection would violate
this rule. Rebuild and upsert FTS from role plus `Message.Content` only. Make
the fallback deserialize bounded candidates and compare title, folder, tags,
role, and `Content` rather than matching raw JSON. This needs no new SQLite
column. JSON backup/export retains the field for lossless recovery.

Acceptance criteria:

- old conversation rows load unchanged and a new reasoning message round-trips;
- switching branches shows the reasoning belonging to that exact assistant
  node, with no copying between siblings;
- compatible enabled history serializes `reasoning_content`; unsupported,
  unknown, and disabled cases omit the JSON property;
- truncation includes reasoning only under the same wire predicate;
- FTS, Recall, memory markers, speech, and ordinary Copy never receive it.

## 5.4 Full `--reasoning-preserve` control

Add `ServerConfig.PreserveReasoning`, default true, and nullable
`ModelProfile.DefaultPreserveReasoning`. Expose one checkbox in the Services
model section labelled `Preserve reasoning between turns`. A proven compatible
new model defaults its profile to true; Services Save Config writes the current
choice back through the existing model-profile flow.

The checkbox is enabled only when the effective capability says the selected
runtime supports the flag and the selected template reports
`supports_preserve_reasoning=true`. It is checked by default for a newly proven
compatible model. Unknown shows `Waiting for a successful model capability
probe`; Unavailable shows the evidence and disables the control. Changing the
main model clears capability presentation immediately while preserving each
model's own saved default through its profile.

Launch rules:

- enabled plus proven support emits `--reasoning-preserve` exactly once;
- disabled plus proven support emits `--no-reasoning-preserve` exactly once;
- Unknown emits neither and relies on llama-server's template default for the
  first launch. The healthy `/props` probe then resolves the UI without an
  automatic restart. If support is found and the checkbox is on, show `Restart
  to apply` and do not replay reasoning until the active-process launch snapshot
  proves `--reasoning-preserve` was applied;
- an executable without the paired flags emits neither;
- ExtraArgs cannot silently provide a contradictory duplicate. Apply the same
  first-class-option precedence and trust warning used by existing engine
  flags.

This setting controls both launch behavior and whether Hermaeus replays stored
reasoning on later requests. Turning it off does not erase existing reasoning.
The replay predicate uses the active launch snapshot, not merely the saved
checkbox, so the request and process cannot disagree during that first probe.

Acceptance criteria:

- compatible true/false configurations produce the exact paired arguments;
- first launch in Unknown uses upstream template default, then a matching
  `/props` result enables the checkbox and requests a restart without changing
  the running process or prematurely replaying reasoning;
- switching model or executable cannot reuse a capability result with a
  different identity;
- turning preservation off stops history replay but leaves transcript and JSON
  reasoning intact;
- settings save remains atomic and no second save path is introduced.

## 5.5 Transcript and export presentation

In each assistant card with reasoning, place a `Reasoning` toggle above the
answer. It is collapsed by default, includes a small `Thinking` live state
during reasoning-only streaming, and uses the existing Markdown renderer when
expanded. It must remain usable by keyboard and screen reader. Do not use
low-contrast italic text as the only distinction.

The existing Copy and Read aloud actions remain answer-only. Add `Copy
reasoning` inside the expanded section with a tooltip. Markdown conversation
export writes a clearly labelled collapsible-independent `Reasoning` subsection
before that assistant's `Answer`; JSON naturally includes the additive field.
The consolidated Chat export action from doc 03 still chooses the format.

Empty final answer behavior is explicit:

- reasoning still renders and persists;
- the answer area states `No final answer was returned.`;
- benchmark scoring evaluates final `Content`, so previous reasoning-only empty
  runs remain failures rather than being rewritten or reclassified.

Acceptance criteria:

- ordinary messages have no empty reasoning chrome;
- reload starts collapsed and preserves exact reasoning text;
- Copy, Copy reasoning, Markdown export, JSON export, and speech have distinct
  fixture assertions;
- the shared icon-tooltip guard covers the new action;
- no UI copy uses `thoughts` and `answer` interchangeably.

## Tests and documentation

Budget 30 to 40 focused tests: GGUF scalar parsing, executable help facts,
`/props` capability merge/cache invalidation, argument precedence, three
provider stream shapes, dual accumulation and timing, cancellation,
persistence/mapping/branching, history replay and token budgeting, exclusion
from derived systems, local API round-trips, exports, and UI guards.

Update `docs/features.md`, `docs/llama-cpp-features.md`, the Chat and Services
workflow descriptions, `docs/benchmarks.md`, and `CHANGELOG.md`. Replace the old
survey statement that this feature belongs to a future round with the exact r30
contract and its `Unknown` behavior.

## Verified implementation evidence

The scope above is based on the installed b10227 `llama-server --help` output
and current upstream llama.cpp contracts checked on 2026-08-20:

- GGUF defines `{architecture}.nextn_predict_layers`, and llama.cpp's model
  layer names the matching NextN tensors;
- `GET /props` returns `chat_template_caps` and `modalities`;
- `--reasoning-format deepseek` returns separate `reasoning_content`;
- `--reasoning-preserve` and `--no-reasoning-preserve` are compatible with
  templates reporting `supports_preserve_reasoning`;
- llama.cpp's own streaming test client accumulates
  `delta.reasoning_content` and sends prior assistant
  `reasoning_content` beside content and tool calls.

The implementation PR must record the installed build's exact help fixtures in
tests. Do not make tests depend on live upstream pages.
