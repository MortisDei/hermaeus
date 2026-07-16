# 02 - Provider correctness

The LLM provider layer and the benchmark engine that sits on it.

## 2.1 Ollama chat does not stream

`OllamaService.StreamChatAsync` (src/Aether.Services/OllamaService.cs:147-149)
uses `PostAsJsonAsync`, which completes only after the entire response
body is buffered (`ResponseContentRead` default). With `stream = true`
the server chunks tokens for the whole generation, so the method
returns only when generation is finished and then replays the buffered
lines: no incremental tokens, first-token latency equals total latency,
and Stop/cancel cannot interrupt mid-generation cleanly. LlamaCpp and
OpenAI services already do this correctly via
`SendAsync(..., HttpCompletionOption.ResponseHeadersRead, ct)`.
Additionally, `resp.EnsureSuccessStatusCode()` throws out of the
iterator instead of yielding `LlmStreamEvent.Error(...)` the way the
other two providers do, so an unreachable Ollama endpoint surfaces as
an exception to the enumerator rather than an in-stream error message.

Acceptance criteria:

- Request built with `HttpRequestMessage` + `ResponseHeadersRead`;
  a fake handler test asserts the first `LlmStreamEvent` is yielded
  before the response stream completes.
- HTTP failure yields an error event (matching LlamaCppService's
  behavior) instead of throwing from enumeration.

## 2.2 OpenAiService mutates shared static HttpClient headers per call

`AuthAsync` (src/Aether.Services/OpenAiService.cs:38-40) assigns
`_http.DefaultRequestHeaders.Authorization` on the shared static
client on every models/chat call. Default headers on HttpClient are
not safe to mutate while requests are in flight; concurrent chat +
model refresh can race, and any future second key (runtime profiles
carry their own ApiKey) would leak between requests.

Fix direction: set `req.Headers.Authorization` per request (the code
already builds `HttpRequestMessage` for chat; `GetModelsAsync` needs
the same), delete `AuthAsync`.

Acceptance criteria:

- No writes to `DefaultRequestHeaders` outside construction; both call
  sites send per-request auth (fake-handler test asserts the header on
  the captured request).

## 2.3 "OpenAI-compatible" model list only shows OpenAI-branded ids

`GetModelsAsync` (OpenAiService.cs:51-55) filters to ids starting with
`gpt`/`o1`/`o3`/`o4`. The provider descriptor and settings UI call this
"OpenAI-compatible", but pointing `OpenAiBaseUrl` at LM Studio, Groq,
OpenRouter, Mistral, vLLM etc. yields zero models (`llama-3.3-70b`,
`mistral-large`, `claude-*` via proxies are all filtered out), making
the feature unusable for the compatible-endpoint case it is named for.

Fix direction: drop the prefix filter; if noise on the real OpenAI
endpoint is a concern, filter only when the base URL host is
`api.openai.com`, and even then prefer a deny-list of known non-chat
ids (embeddings, tts, whisper, dall-e) over an allow-list of prefixes.

Acceptance criteria:

- A models payload of non-gpt ids surfaces all chat-usable ids;
  api.openai.com behavior (if special-cased) covered by its own test.

## 2.4 Cold model cache misroutes chat to llama.cpp

`CompositeLlmService.DescribeModel` (CompositeLlmService.cs:58-65)
falls back to the llama.cpp descriptor when the id is not in
`_providerTagsByModelId`, and `StreamChatAsync` (189-197) routes by
that answer. The cache is only populated by `GetModelsAsync` and
expires after 300 s. A send with a saved OpenAI model id before/without
a model refresh (or after cache expiry when providers are unreachable,
since failures cache nothing) silently posts the conversation to the
local llama-server. Ollama ids survive only because of their `ollama:`
prefix special case.

Fix direction: remember the id->tag mapping durably (persist last-known
tags per model id in memory beyond cache expiry, or infer from the
conversation's stored Provider), and when the tag is genuinely unknown,
prefer an explicit error event over silently choosing a provider.
Related minor: when every provider returns empty, `_cachedModels`
stays empty and `_cacheUntilUtc` is still advanced only on success
paths through the same write block; verify a full-failure scan does not
re-probe on every call (negative caching), and cover it.

Acceptance criteria:

- With an empty/expired cache, streaming a model id whose tag was known
  in a previous scan routes to the correct provider (test:
  populate, expire, stream).
- A never-seen id yields an explicit error event, not a silent
  llama.cpp post.
- All-providers-down scan does not turn every subsequent
  `GetModelsAsync` call into a fresh 5 s probe storm (bounded by a
  short negative-cache TTL).

## 2.5 Probed context length goes stale across model switches

`LlamaCppService._contextLengthCache` (LlamaCppService.cs:14, 71-100)
is static, keyed by base URL, cleared only when it exceeds 50 entries.
Restarting the managed server with a different model or `--ctx-size`
leaves the old `n_ctx` cached forever, and
`ModelProfileService.ApplyProfiles` (ModelProfileService.cs:61) feeds
it into `DefaultContextSize`, so token-budget math uses the previous
model's window. `CompositeLlmService.InvalidateModelCache` does not
touch it. `OllamaService._contextLengthCache` has the same lifetime but
is keyed by model name, which bounds the damage.

Fix direction: invalidate the llama.cpp entry when
`InvalidateModelCache` fires and whenever a managed server transitions
to Running (ServicesViewModel already invalidates the model cache on
server lifecycle; verify and extend), or simply re-probe when the
`/v1/models` id differs from the id cached alongside the length.

Acceptance criteria:

- Test: probe returns 4096, cache invalidated via the chosen hook,
  next GetModelsAsync re-probes and returns the new value.

## 2.6 The benchmark judge is a phantom feature

`BenchmarkSuite.UseJudge`/`JudgeModelId` are editable in
BenchmarkView.axaml, copied into every run
(BenchmarkService.cs:148-149), exported, and re-imported by
`RerunAsync` - and no code path ever invokes a judge model.
r2 established the "phantom setting" class; r7 established the
project's precedent that behavioral evals use deterministic checks,
not LLM judges.

Recommendation: remove the UI fields and stop copying the properties
(keep the model properties themselves so stored suite/run JSON still
deserializes), with a one-line CHANGELOG note. If the owner would
rather implement judging, that is a feature round item, not a bug fix,
and must be specced separately; do not half-implement it here.

Acceptance criteria:

- No UI control binds UseJudge/JudgeModelId; stored suites with the
  fields still load (round-trip test); features.md no longer implies a
  judge exists (verify wording).

## 2.7 Rerun drops case tags

`RerunAsync` (BenchmarkService.cs:207-218) rebuilds cases from stored
results but never copies `Tags`, so reruns produce untagged results and
fall out of the r5/r6 per-tag insights (the suite-join fallback in
BenchmarkInsightsService only rescues them while the original suite
still exists and case ids match).

Acceptance criteria:

- Rerun of a tagged run produces tagged results (unit test through
  ScoreDeterministic's Tags copy).

## 2.8 Runtime-profile health checks send the secret reference as the bearer token

`RuntimeProfileService.CheckHealthAsync` (RuntimeProfileService.cs:64-65)
sends `profile.ApiKey` verbatim. When the key was stored through
`ISecretStore` the setting holds `secret:<name>`, so the health check
authenticates with the literal reference string and fails against any
endpoint that requires auth.

Acceptance criteria:

- Health check resolves via `ISecretStore.ResolveAsync` before sending
  (constructor gains the dependency; fake-store test asserts the
  resolved value in the header, and that a non-reference plain value
  passes through unchanged).
