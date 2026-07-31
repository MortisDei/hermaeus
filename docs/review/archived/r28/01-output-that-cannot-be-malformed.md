# 01. Output that cannot be malformed

## Why

The owner asked, half seriously, how you make a small model smarter. You do
not. You make the wrong answers unrepresentable.

`MemoryExtractionService` is the clearest example in the codebase of what
happens when you skip that. It asks a model for structured extraction and
then defends against the answer three times over:

- `JsonSerializer.Deserialize<StructuredExtractionResult>` on the response
  (`MemoryExtractionService.cs:94`)
- when that throws, a salvage pass that hunts for a JSON object inside the
  text and runs `JsonDocument.Parse` on the candidate (`:171`)
- when that finds nothing, `MemoryMarkerRegex` over `[MEMORY: ...]` markers
  (`:14`, with `:23` and `:27` for update and forget)

`docs/features.md:193-196` describes the arrangement honestly: auto-summary
"asks for structured JSON ... the marker format remains the fallback if a
model doesn't follow the JSON instruction". A 4B model does not follow the
JSON instruction reliably. That is not a defect in the model, it is a
category error in the request: the app is asking politely for something it
could be enforcing.

llama.cpp has enforced it for years. The installed server exposes it:

```
--grammar GRAMMAR       BNF-like grammar to constrain generations
--grammar-file FNAME    file to read grammar from
-j, --json-schema SCHEMA   JSON schema to constrain generations
-jf, --json-schema-file FILE
```

verified by running `C:\AI\llama-server\b10195\llama-server.EXE --help`.
A search across `src/` for `grammar`, `json_schema` or `response_format`
returns two hits, both `response_format: "wav"` in the voice path
(`LocalAiSetupScriptGenerator.cs:38`, `OpenAiVoiceProvider.cs:167`). The
chat path has never sent a constraint of any kind.

Constrained decoding is the highest-leverage thing available to this app for
small-model reliability, it needs no new dependency, and every provider
Hermaeus talks to supports some form of it.

## What this is not

This does not make the model's *judgement* better. A constrained 4B still
picks the wrong category and writes a mediocre summary. It makes the
model's *shape* correct by construction, which removes an entire class of
failure that currently costs three parsers and a silent quality drop when
all three miss.

## Work items

### 1.1 A structured-output contract on `LlmChatOptions`

`LlmChatOptions` (`ILlmService.cs:69`) already carries the sampling surface
and its summary says to extend the record rather than adding parameters to
`StreamChatAsync`. Add one nullable property:

```csharp
/// <summary>
/// Constrains generation to a shape. Null means unconstrained, which is
/// the behaviour every caller had before r28. Providers that cannot
/// enforce a constraint report that through
/// <see cref="LlmStreamEvent"/> rather than ignoring it.
/// </summary>
public LlmOutputConstraint? OutputConstraint { get; init; }
```

with a new record in `Hermaeus.Core.Models`:

```csharp
public sealed record LlmOutputConstraint
{
    public string? JsonSchema { get; init; }
    public string? Grammar { get; init; }
    public string Description { get; init; } = string.Empty;
}
```

Exactly one of `JsonSchema` and `Grammar` is set; a constraint with both or
neither is invalid and the factory refuses it. `Description` is a short
human-readable label ("memory extraction v1") that exists so traces and the
Context Inspector can say what was enforced without printing a schema.

Provide `LlmOutputConstraint.FromJsonSchema(string)` and
`LlmOutputConstraint.FromGrammar(string)` as the only construction paths.

Tests: valid construction each way; both-set and neither-set both refused;
the record round-trips through `System.Text.Json` (it goes into traces).

### 1.2 The llama.cpp provider sends the constraint per request

`LlamaCppService` gains constraint serialization on its completion request.

**Verify the field names against the running server before writing this.**
What this pack verified is the *command line*, which is a different surface
from the request body. Start the managed server and POST a minimal request
with a constraint, then confirm the response is actually constrained by
sending a prompt that would otherwise produce prose. A field the server does
not recognise is ignored silently, and unconstrained output that happens to
parse looks exactly like a working implementation. This is the same trap r27
documented for `--draft-max`, which had been removed upstream and printed a
notice while doing nothing.

The check that proves it works: constrain to a schema requiring an object
with one integer property, prompt with "write a poem about the sea", assert
the response parses as that object. A model that returns a poem means the
constraint did not reach the sampler.

### 1.3 The other two providers, honestly

`OpenAiService`: OpenAI-compatible servers accept `response_format`, and
support varies by server and model. Send it when configured; when the server
rejects it, surface the rejection rather than retrying unconstrained.

`OllamaService`: Ollama accepts a `format` field taking a JSON schema.

For any provider or model that cannot constrain, the call must fail visibly
at the point of use or return a `LlmStreamEvent` carrying the reason. It
must not quietly send an unconstrained request, because the caller's whole
reason for setting a constraint is that it intends to parse the result
without defending against prose.

`CompositeLlmService` routes by provider tag (`CompositeLlmService.cs:14`)
and needs no constraint-specific logic beyond passing `options` through,
which it already does.

Tests: each provider serializes the constraint into the shape that provider
expects (assert on the serialized request body, not on a live server); a
provider that cannot constrain produces a named refusal rather than an
unconstrained request.

### 1.4 A capability flag, so callers can ask before they commit

`LlmModel` gains `bool SupportsOutputConstraints`. It is set from what the
provider knows about itself, not probed: llama.cpp true, Ollama true,
OpenAI-compatible true when the configured endpoint declares it and false
otherwise.

This exists so 1.5 can choose its path before sending rather than
discovering mid-parse, and so the Context Inspector can show whether the
turn's output was constrained.

**Not a probe.** r26 rejected making the capabilities endpoint probe for the
same reason: a capability check that loads a model to find out whether a
model loads is a denial-of-service handle wearing a health check's name.

### 1.5 Memory auto-summary asks for a schema

`MemoryExtractionService`'s structured path emits a JSON schema for
`StructuredExtractionResult` and sets it as the request's constraint when
the selected model reports `SupportsOutputConstraints`.

Emit the schema by hand as a `const string` beside the type it describes.
Do not add a schema-generation package, and do not reflect over the type at
runtime: the schema is four properties, it changes when the record changes,
and a test asserting the two agree is cheaper and more legible than a
generator.

**All three existing fallbacks stay exactly as they are.** They still run
for unconstrained providers, for older local servers, and for the OpenAI
path when the endpoint declines. What changes is that on the owner's own
setup they stop being reached.

Tests: the emitted schema and `StructuredExtractionResult` agree property
for property (this is the test that fails when someone adds a field);
constrained path is taken when the model supports it; unconstrained path is
byte-identical to today's behaviour; a malformed response still falls
through all three layers exactly as before.

### 1.6 The receipt says whether the shape was enforced

The Chat Trace Viewer already records selected model, runtime, system
prompt, attachment count and token estimate (`features.md:1044-1047`). Add
the constraint's `Description`, or "unconstrained", to that record.

One line, no new panel. It is the only way to tell, after the fact, whether
a turn that parsed cleanly did so because the model complied or because it
was made to.

## Deliberately out of scope

**The agent's planner protocol**, which is doc 05 and is this contract's
largest consumer. Doc 01 builds `LlmOutputConstraint` and proves it on
memory extraction, which is small and self-contained. Doc 05 points it at
`AgentService`'s JSON action protocol, where the same fragility costs an
agent run rather than a memory. They are separate documents because 1.2's
verification risk (does a per-request constraint actually reach the sampler)
should be settled against the cheap consumer before the expensive one is
built on top of it.

**A user-facing grammar editor.** `Grammar` exists on the record because
llama.cpp's own surface has it and a future caller may want a non-JSON
shape. No UI points at it this round.
