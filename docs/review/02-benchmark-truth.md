# 02 - Benchmark truth: real timings, honest scores, honest metadata

The Benchmarks page presents itself as a measurement tool; several of
its numbers are currently approximations or placeholders presented
without qualification. This doc makes the measured things measured and
the unmeasured things visibly labeled or removed.

## 2.1 Use real server timings instead of chars/4

`BenchmarkService.RunCaseAsync` (`src/Aether.Services/BenchmarkService.cs:320-407`)
streams via the text-only `StreamChatTextAsync` and computes
`ApproxTokensPerSecond = (chars / 4) / totalSeconds`
(`FillTiming`, `:759-767`). Meanwhile `LlamaCppService` has parsed the
llama-server `timings` object (prompt_n, prompt_ms, predicted_n,
predicted_ms) on the final streamed chunk since r10
(`LlamaCppService.cs:325-338`) and `ChatSendOrchestrator` already
consumes it for chat. The benchmark throws it away.

- Switch `RunCaseAsync` to `ILlmService.StreamChatAsync` events: append
  `ContentDelta`s exactly as today, stamp `FirstTokenMs` on the first
  non-empty delta, and capture the last non-null `evt.ServerTimings`.
- New additive fields on `BenchmarkResult`
  (`src/Aether.Core/Models/BenchmarkModels.cs:114-154`):
  `int? PromptTokens`, `double? PromptMs`, `int? GeneratedTokens`,
  `double? DecodeMs`, `string MeasurementSource` (values
  `"server-timings"` / `"chars-approx"`; default empty keeps old JSON
  loading cleanly).
- When timings are present: `ApproxTokensPerSecond` (keep the property
  name for JSON/back-compat, it feeds every aggregate) becomes
  `predicted_n / predicted_ms * 1000`, and prompt speed
  (`prompt_n / prompt_ms * 1000`) is stored for display. When absent
  (Ollama, OpenAI-compatible, remote), keep the chars/4 fallback and
  stamp `chars-approx`.
- Surface the source: `BenchmarkResultViewModel.Timings`
  (`BenchmarkViewModel.cs:553`) and the markdown export show either
  `43.1 tok/s (server-measured)` or `~9.2 tok/s (estimated from characters)`
  plus prompt speed when known.

Acceptance: a fake `ILlmService` emitting deltas plus a final
timings-bearing event produces a result whose tok/s equals the timings
math (not the chars math) and whose `MeasurementSource` says so; a fake
without timings reproduces today's numbers with `chars-approx`.

## 2.2 Decode-window denominator for the fallback path

The chars/4 fallback divides by *total* elapsed time including prompt
processing (`FillTiming` `:764-766`), so any model given a long prompt
is reported slow twice: once in `FirstTokenMs` and again in tok/s. This
also feeds `RankingScore`'s speed component
(`BenchmarkModels.cs:82`, `BenchmarkScoring.RankingScore` `:203-210`),
biasing rankings against long-prefill runs.

Fix: fallback tok/s uses `max(totalMs - firstTokenMs, 1)` as the
denominator. `CharsPerSecond` may keep total-time semantics (it is
labeled as such); only the tokens/sec figure changes. Stored old runs
keep their persisted per-result numbers (no migration, doc 03).

Acceptance: fallback case with `firstTokenMs = 4000, totalMs = 5000`
and 400 chars yields 100 chars over ~1 s of decode = ~25 tok/s
(chars/4), not ~20.

## 2.3 Stop scoring noise as "resource"

`FillResources` (`BenchmarkService.cs:769-779`) computes
`ResourceScore` from the *Aether process's* RSS delta. The model runs
in llama-server (a different process) or on a remote endpoint, so this
term is noise with a 10% weight in `RankingScore`
(`BenchmarkScoring` `:203-210`). The VRAM before/after fields are
captured but unused in scoring, and a per-case VRAM delta for an
already-loaded model is ~0, so there is no cheap honest signal here.

Fix: set per-result `ResourceScore = 1.0` (neutral) always; keep the
before/after memory and VRAM snapshot fields for display; keep the
scoring weights untouched so the resource term becomes a constant and
historical `RankingScore` values (recomputed from stored results at
read time) shift minimally. Update the doc comment on
`BenchmarkScoring.RankingScore` to say the resource slot is reserved
and currently neutral. Do not invent a new resource metric this round
(doc 03 rejections).

## 2.4 Honest run metadata

`CreateMetadata` (`BenchmarkService.cs:781-810`) stamps
`RuntimeKind = "dotnet"`, `Quantization = ""`, `GpuLayers = null`,
`ModelPath = ""`, `Threads = Environment.ProcessorCount` (the app's
CPU count, not the server's `--threads`) on every run. But
`BenchmarkInsightsMath` groups aggregates by
`ModelId|Quantization|RuntimeKind`
(`BenchmarkInsightsModels.cs:116`) and the Insights UI displays
`ModelName (Quantization)` (`BenchmarkViewModel.cs:565-567`), so the
grouping key is degenerate and the quantization label has never
rendered for a live run.

- `RuntimeKind`: the actual runtime (`"llama.cpp"`, `"ollama"`,
  `"openai-compatible"`), derived from the model's provider tag.
  Legacy compatibility: `BenchmarkInsightsService.LoadReportAsync`
  gains a load-time normalization (like its existing `ResolveTags`
  pass, `BenchmarkInsightsService.cs:60-77`) mapping stored `"dotnet"`
  to the run's provider-derived kind so old and new runs of the same
  model keep aggregating together.
- `Quantization`: for a local `.gguf` model, from
  `GgufMetadataReader` `general.file_type` (doc 01.1). Non-local
  providers keep empty.
- `ContextSize`: for a managed-GGUF model, the managed Chat
  `ServerConfig.ContextSize` actually launched; else
  `model.DefaultContextSize` as today (`:789`).
- `GpuLayers`/`Threads`/`ModelPath`: from the same managed
  `ServerConfig` when applicable; otherwise leave null/empty rather
  than stamping app-process values. `Threads` must stop defaulting to
  `Environment.ProcessorCount`.

Acceptance: a run against a managed GGUF model carries the server's
real context/layers/threads/path and a non-empty quantization; Insights
aggregation over a mixed old("dotnet")+new store still produces one
group per model.

## 2.5 Rerun fidelity

`BenchmarkService.RerunAsync` (`BenchmarkService.cs:192-223`)
reconstructs the model as `new LlmModel { Id, Name, Provider }`
(`:221`), losing `DefaultContextSize`, tags, and profile linkage; the
rerun's metadata (2.4) would be hollow again. Fix: resolve the id
against `_llm.GetModelsAsync()` first and use the live instance when
found; fall back to the reconstruction (with its limitations) only when
the model no longer exists. One test each way.

## 2.6 Cold means cold (reproduce first)

`RunAsync` stamps `RunMode = "Cold"` with a comment
(`BenchmarkService.cs:137-144`) asserting that with one iteration per
case "every pass is genuinely cold". Since r14, `LlamaCppService`
sends `cache_prompt: true` unconditionally (`LlamaCppService.cs:276`)
and the managed server runs a single slot with `--cache-reuse 256`
(`ServerProcessManager.BuildLaunchArguments`, `:407-418`), so the
server retains the previous request's KV: re-running a suite
back-to-back can give the first case a warm prefill while being
reported as Cold.

Reproduce first: against a live managed server, run a one-case suite
twice in a row and compare `FirstTokenMs`/`PromptMs`; a large drop on
the second "Cold" run confirms the leak. Then:

- Add `bool DisablePromptCache` (default false) to `LlmChatOptions`
  (`ILlmService.cs`, near the r13 sampling fields), honored only by
  `LlamaCppService` as `cache_prompt = !options.DisablePromptCache`.
  Other providers ignore it.
- Benchmark sets it for iteration 0 (Cold phase) and leaves it false
  for Warm iterations, which keeps the r5 warm-phase semantics working.
- Chat path untouched: `cache_prompt: true` stays the chat default
  (doc 03 rejections).
- Update the `RunMode` comment to describe the new mechanism.

Acceptance: payload-builder test asserting `"cache_prompt":false` when
the option is set (mirror `LlamaModelsFetchTests`
`BuildChatPayload_sets_cache_prompt_true_in_snake_case`), plus a
benchmark test asserting the option is set on iteration 0 and not on
iteration 1.

## 2.7 Stop restarting the chat server from a dropdown

`BenchmarkViewModel.OnSelectedModelChanged`
(`src/Aether.ViewModels/BenchmarkViewModel.cs:401-409`) fire-and-forgets
`AutoSwitchSelectedModelAsync`, which calls
`_services.SelectChatModelAndRestartAsync` (`:443-474`). Merely
browsing the Benchmarks model dropdown stops and restarts the live
managed chat server (a 1-2 minute operation on large models) and
repoints `Llm.DefaultModel`, before Run was ever clicked.

Fix: delete the eager switch. `RunAsync` already calls
`PrepareSelectedModelAsync` (`:136`) at run time, which performs the
same switch when actually needed. On selection change, set a passive
`Status` hint instead when the selection is a managed GGUF that is not
the currently served model, e.g.
`Will start managed llama.cpp for <name> when the benchmark runs.`
Keep `OnSelectedModelChanged`'s CanExecute notifications.

Acceptance: a VM test with a spy `ServicesViewModel` (or a counter on
the existing seam) asserting model selection alone triggers no restart
call and Run triggers exactly one.
