# Benchmarks & System Overview

## Benchmarks

The **Benchmarks** workspace runs local prompt suites against selected models and
stores immutable run history under the Hermaeus data root. It discovers running
provider models and GGUF files under the configured AI assets root so users do
not need to paste each local model path by hand.

Benchmarks are intended for practical local model comparison, not lab-grade
hardware benchmarking.

### Benchmark Runs

Runs record the following metrics and metadata:

- First-token latency
- Total latency
- Tokens/sec, measured from the provider's own prompt/decode timings when it
	reports them (llama.cpp), or estimated from output length otherwise. The
	distinction is recorded per result (`server-timings` vs `chars-approx`)
	but not yet surfaced in the UI itself - treat displayed tok/s as
	provider-measured only when you know the provider reports real timings.
- Deterministic quality checks
- Resource deltas (CPU, memory, storage changes) for display; the scoring
	weight for this slot is neutral (reserved, not currently a real signal -
	see Resource Sampling Notes)
- Pass rate, failure count, and weighted rankings
- Run metadata including model identity, backend/runtime, context size, GPU
  layers, generation and prompt thread counts, model path, quantization, KV
  cache K/V types, and Flash Attention (all sourced from the managed server
  actually serving a local GGUF model, not app-process values)
- Persistent empirical profile fingerprints over the material model and
  inference configuration, plus a shared direct-observation source reference.
  The historical v1 fingerprint remains readable. New runs also carry a v2
  composition of runtime, model, hardware, and configuration identity whose
  stable id excludes local paths. These associate the run with what was
  actually measured. They are not a generic capability score or an automatic
  model recommendation.
- Suite version, case version, scoring profile, and run mode
- Cold-only single-iteration runs, or cold and warm phase attempts when suites
	use repeated iterations per case

Runs are exported to JSON, Markdown, and CSV so the full metadata set can be
reviewed later.

### Recorded run metadata, field by field

The prose above describes the shape; this is the actual set, and a guard test
(`DocsCoverageGuardTests`) fails if a field is added to `BenchmarkMetadata`
without appearing here.

`AppVersion`, `ModelPath`, `ModelHash`, `Quantization`, `Backend`,
`RuntimeVersion`, `RuntimeKind`, `ContextSize`, `PromptTemplate`,
`SamplerSettings`, `Temperature`, `TopP`, `TopK`, `RepeatPenalty`, `Seed`,
`GpuLayers`, `Threads`, `PromptThreads`, `BatchSize`, `EmbeddingModel`, `RerankerEnabled`,
`KvCacheTypeK`, `KvCacheTypeV`, `FlashAttention`, `OS`, `CPU`, `RAM`, `GPU`,
`SpeculativeTypes`, `SpeculativeDraftModel`,
`SpeculativeNMax`, `SpeculativeNMin`, `SpeculativePMin`,
`SpeculativeDraftGpuLayers`, `ProfileFingerprint`, `ProfileFingerprintV2`,
`ObservationSource`.

KV cache and Flash Attention are configuration provenance, not a score or a
recommendation. New local-GGUF runs record the managed server values in their
saved record, run details, and JSON, Markdown, and CSV exports. Historical
runs and runs that do not resolve to a managed llama-server show the settings
as not recorded; Hermaeus does not infer a default after the fact.

New runs save both the compatibility v1 fingerprint and a v2 fingerprint over
runtime, model, hardware, and configuration sub-identities. The v2 runtime uses
the selected executable hash where a managed runtime is available. The model
uses verified hash or manifest identity when already known, with file metadata
explicitly marked as a weaker fallback. Unknown extra runtime arguments make
the configuration incomplete and are not persisted verbatim. The associated
observation source is local direct evidence for that one run, not a claim that
the model behaves the same way on another machine or workload. Historical runs
keep their absent or v1 identity rather than being reconstructed from presumed
defaults.

The default action is a one-click benchmark pass. With **Run all suites**
enabled, Hermaeus runs every built-in suite for the selected model. Turning it off
runs only the highlighted suite. Selecting a discovered local GGUF model in the
dropdown only updates a status hint; the managed chat `llama-server` is switched
to that model (restarted if a different model is currently loaded, started if
stopped) only when **Run** is actually clicked, so browsing the dropdown never
triggers a 1-2 minute restart on its own.

### Reproducibility in Benchmarks

Benchmarks are designed for repeatable, practical comparison across models,
runtimes, and hardware profiles.

Hermaeus records run metadata such as model identity, quantisation, runtime,
sampler settings, context size, hardware summary, app version, and suite
version so results can be compared fairly over time.

Quality checks are deterministic where possible and are versioned with the
benchmark suites. Performance metrics such as latency and throughput are
measured observations and may vary due to system load, model warmup, cache
state, GPU clocks, thermals, and runtime behaviour.

For more stable results, use repeated runs and compare median values rather
than single-run measurements.

### Cold and Warm Runs

Benchmarks distinguish cold-start measurements from warm-run measurements where
possible.

- Cold runs have no prior KV cache state for the prompt. Since llama-server
	keeps prompt-cache reuse on by default (for fast follow-up chat sends),
	Hermaeus explicitly disables it for each case's first (Cold) iteration so a
	suite run immediately after a previous one cannot get a warm prefill from
	leftover KV state while still being reported as Cold.
- Warm runs are only reported when a suite uses more than one iteration per
	case; those later iterations keep the prompt cache enabled and can reuse
	cached prefill state from the first run.
- Repeated iterations help separate cache-state effects from steady-state speed.

### Built-In Starter Suites

Hermaeus includes starter suites covering:

- Speed smoke tests for latency and throughput
- Instruction following for formatting and direct instruction adherence
- Light reasoning for short local reasoning tasks
- RAG answer style for grounded, citation-friendly responses
- Refusal behaviour for insufficient-context safety checks
- Coding assistant checks for logs, configuration, and safe next steps
- Context pressure checks for medium prompt summarisation and trade-offs
- Code generation checks for structurally plausible function output
- Structured output stress checks for nested JSON and enumerated formats
- Multi-step reasoning checks for intermediate working and final answers
- Hermaeus workflow checks for summary, memory marker, and system prompt tasks
- Hallucination resistance checks for uncertainty on fictional or unverifiable
	claims

### Suite Versioning

The 0.37.0-alpha deterministic scorer is versioned separately from stored run
data. It accepts multiline structural regexes, normalizes grouping separators
only inside digit runs, recognizes explicit inability phrases, and supports
additive all-required keyword alternative groups. Historical `run_json` rows are
not rescored. A reasoning-only response still fails cases that score the final
answer, because an explanation is not an answer.

Suites and cases carry version identifiers so historical runs remain meaningful
after prompts, scoring profiles, or evaluation rules change.

Example:

```json
{
	"suiteId": "instruction-following",
	"suiteVersion": "1.2.0",
	"caseId": "json-only-response",
	"caseVersion": "1.0.1",
	"expectedBehaviourVersion": "1.0.1",
	"scoringProfile": "coding-helper-v1"
}
```

### Failure Tracking

Benchmark runs record failures explicitly rather than skipping them. Common
failure categories include:

- timeout
- model load failure
- connection failure
- empty response
- malformed response
- quality check failed
- refusal mismatch
- context overflow
- OOM or VRAM failure
- cancelled by user

These categories help distinguish model quality issues from infrastructure or
resource problems.

### Iterations and Summary Stats

One run is a data point. Several runs are a benchmark.

Each case can run multiple iterations. The benchmark UI reports summary stats
such as:

- Median first-token latency
- Min and max latency
- P95 latency
- Median tokens/sec
- Standard deviation
- Failure count
- Pass rate

The UI keeps the display simple while preserving the raw iteration results for
inspection and export.

### Benchmark Categories

The starter suites are organised by purpose:

- Performance suites: first token, throughput, long context, memory pressure
- Behaviour suites: instruction following, refusal behaviour, formatting,
	structured output, hallucination resistance, tool-call style
- RAG suites: grounded answer style, citation correctness, refusal when
	unsupported, source faithfulness
- Coding suites: patch explanation, small bug diagnosis, config troubleshooting,
	command suggestion safety, structural code generation
- Hermaeus workflow suites: conversation summarisation, memory marker extraction,
	and assistant system prompt generation

For Hermaeus specifically, a local dev assistant suite should also cover log,
config, and code excerpt analysis with safe next-step suggestions and no
invented APIs.

### Ranking Profiles

Different users care about different trade-offs, so benchmark rankings should
use named profiles instead of one universal score.

- Fast Chat: speed weighted high, quality moderate
- Coding Helper: instruction following and factuality weighted high
- RAG Answering: citation correctness and grounding weighted high
- Low VRAM: memory usage and stability weighted high

### Comparing Models Fairly

The Insights tab's **Best overall** card ranks models only on the cases every
ranked model has actually run, keyed on both case id and case version, and says
what that basis was ("across 24 case(s) run by all 3 ranked model(s)").

This matters because a per-model average is not a comparison. Before r25 each
model was averaged over whatever cases it happened to have run, gated only by a
volume floor of 2 runs and 10 cases, so a model that ran one short easy suite
could outrank a model that ran everything.

Consequences worth knowing:

- When no two models share enough cases, there is **no** Best overall. The card
  says so and explains that running the same suite on each model is the fix. An
  honest "not enough shared results" is the correct answer, not a fallback.
- A model that has run far fewer of the shared cases is excluded from the
  ranking and reported in the caveats, rather than shrinking the comparison for
  everyone else.
- A single benchmarked model is still ranked over its own cases: there is no
  comparison to be unfair about.
- The card names the axis. The ranking blends quality with speed, so the overall
  leader can be second on quality; when that happens it is stated explicitly.
- Clicking the card opens a per-case breakdown with the runner-up's score for
  the same case beside it, so the ranking can be checked rather than trusted.
- Hermaeus Doctor reads the same report, so its advisory never disagrees with
  the panel. Its advisory is scoped to the shared case set it names; it makes
  no cross-suite claim.

### Best Across Every Suite

Beside Best overall, the Insights tab answers a different question: which
model is best across **all** your suites, rather than across one shared set of
cases.

**The method is mean per-suite standing.** Each suite gets its own leaderboard,
ranked by the same shared-case-set rule above. Suites with a usable leaderboard
then each cast one vote: models are ranked by their mean position across those
suites, ties broken on mean quality-per-second and then on model id, so the
same data always gives the same answer. The card states how many suites the
result rests on, and lists the leader's position in each of them; expanding a
suite row shows that suite's full leaderboard.

The obvious alternative, pooling every case from every suite, is wrong here: a
40 case suite would outvote a 5 case suite eight to one, so "best across all
suites" would silently mean "best on the biggest suite". Ranking per suite and
then averaging gives each suite one vote, which is what the phrase means in
English.

There is deliberately **no answer** when there cannot be an honest one, and the
card says which case applies:

- Fewer than two suites have two models with enough shared cases to rank on.
- No model has run every one of the comparable suites.

Suites and models left out are named: a suite whose models share no cases, a
suite only one model ever ran, and a model that ran three of five suites are
each listed under "Left out of this ranking" rather than quietly dropped.

### Baseline Comparison

Every benchmark run should be compared against a chosen baseline model so the
result is easy to interpret.

Example:

- Baseline: `Mistral 7B Q4_K_M`
- Candidate: `Phi-4 mini Q4_K_M`
- First token: 12% slower
- Tokens/sec: 18% faster
- Pass rate: 7% better
- Memory: 800 MB higher

### Resource Sampling Notes

GPU Fit and Lab use one shared runtime telemetry contract. A sample records its
metric, value or `Unknown`, source, trust state, timestamp, runtime identity,
and process-instance boundary. The current platform source provides the
matching runtime process working set. Per-process GPU memory remains `Unknown`
unless a trustworthy runtime or platform counter is available. Optional
whole-device GPU readings are labelled `DeviceTotal` and are never subtracted
or attributed to the model.

High-frequency series are bounded. Persisted GPU Fit experience keeps
min/max/mean/current/count summaries plus exact early, current, and extrema
samples. Prediction and observation remain separate. Signed discrepancies are
calculated only for exact v2 fingerprint matches and comparable process-scoped
or runtime-reported values; incompatible observations are listed separately and
never rewrite the deterministic formula.

Resource deltas are sampled before, during, and after a run and shown for
reference. They are best-effort and may miss short-lived spikes on some
platforms. The model itself runs in a separate process (`llama-server`) or on
a remote endpoint, so Hermaeus's own process memory delta says nothing about the
model's actual resource use; the resource term in the weighted ranking score
is therefore always neutral rather than computed from that delta. The before/
after memory and VRAM snapshots are still recorded and exported for manual
inspection.

For GPU-capable systems, record:

- VRAM before
- VRAM peak if available
- VRAM after
- GPU utilisation if available
- GPU name
- Probe method
- Probe error if unavailable

### Benchmark Data Retention

Benchmark run history stores prompts, outputs, scores, and runtime metadata
under the Hermaeus data root. Users can export individual runs, delete individual
runs, or clear all saved run history from the workspace after confirmation.

When benchmarking a remote provider, prompt content is sent to that configured
provider.

### Features

- Saved run history for comparison and trend analysis
- Deterministic quality checks for reproducible assessment where possible
- Rankings and weighted scoring profiles
- One-click full-suite benchmark runs
- Local GGUF discovery from the AI assets root
- Managed `llama-server` model switching for discovered local GGUF files
- Reruns for validation
- Export to Markdown, JSON, and CSV formats
- Confirmed clear-history action for saved benchmark runs
- Export all runs into a timestamped folder index or a single zip archive
- Run info dialog: view run-level summary, metrics, and export from a dialog window
- Ranking timeframe filters: view All runs, Latest per model, or Last N runs
- The Rankings tab shows each run's rank and a proportional score bar
  (relative to the top score in the current filter) alongside a Details
  button that opens the same run-info dialog used elsewhere, instead of a
  bare table
- Two-column layout: a left rail stacks Suites, Run Setup, and Run History;
  the right side is one panel with tabs (Rankings, All Results, Insights, and
  Run Detail). Run Detail holds a selected run's case-by-case results and raw
  output - selecting a run in Run History, or a run/rerun completing, switches
  to it automatically so results land next to where you were looking instead
  of a separate panel. Tab content is width-capped and centered rather than
  stretching edge-to-edge on a maximized window.

## Speed Check

A built-in suite, alongside the starter suites, for answering one question:
did a runtime setting change make generation faster on this machine?

It exists because of speculative decoding. Enabling a knob you cannot measure
produces a setting that is believed rather than known, on hardware where the
answer genuinely varies.

**What it measures.** Tokens per second, prompt tokens per second, and time to
first token, taken from llama-server's own `timings` rather than estimated. Four
fixed prompts, chosen because drafting behaves differently across them:
structured output and repetitive output, where a draft's proposals are accepted
often, and code and free prose, where they are accepted less often. Each case
runs five iterations, so a result carries a range rather than a single sample.

**Draft acceptance.** When speculative decoding is active, llama-server reports
how many tokens it drafted and how many the target model accepted, and both are
shown beside the speed. This is the number that separates "drafting engaged and
did not help" from "drafting never engaged". `0 drafted` means the latter, and
means a comparison was run between two identical configurations. A provider
that reports no draft counters shows nothing rather than a zero: a missing
measurement and a measured zero are different facts. No recommendation is
attached to an acceptance rate; `12%` is a fact, "12%, consider disabling
drafting" is not the app's call.

Hermaeus Doctor reports the same thing without needing a benchmark run, across
three separate answers, because they are three separate facts:

- **`0 drafted`**: drafting was configured, the server reported counters, and
  it drafted nothing.
- **No counters at all in a run that did happen**: the server that answered
  was not started with `--spec-type`. llama-server reports draft counters
  whenever speculative decoding is active, so their complete absence points at
  the process rather than at drafting. **Changing the speculative setting does
  not restart a running server**, and the Speed Check benchmarks whatever is
  already listening, so this is the state to expect after toggling the setting
  without restarting from Services.
- **No Speed Check for this model at all**: never measured, which is not the
  same as measured and found dead.

**What it does not measure.** Quality. No case carries an expected keyword, an
expected pattern, or a refusal expectation, because a throughput number should
not quietly become a pass or a fail.

**Prompt cache boundaries.** Cold and warm phases report externally observable
prompt throughput and time to first token. They can show whether the workflow
changed, but they do not claim how many prompt tokens llama-server reused:
this runtime integration has no stable reuse-token counter. A timing difference
is not fabricated into a reuse count. Shared-prefix cache measurement remains a
future controlled workload, not a normal-chat inference.

**Comparing two runs.** Two Speed Check runs of the same suite against the same
model can be shown side by side, with the difference in tokens per second,
prompt tokens per second and time to first token, and the configuration
difference that separates them. Each side reports the median across its
iterations and the range observed, written as
`70.2 tok/s (66.8 to 71.9 over 5 runs)`. If the two ranges overlap, the reader
can see that for themselves, which is the whole point and where the app's job
ends. A run records the speculative settings that
produced it, which is what the comparison keys on. Runs of different models or
different suites are refused rather than compared.

There is deliberately no verdict, grade, score, recommendation or confidence
interval. The app reports what happened; it does not rate itself, and a handful
of runs on a desktop under unknown load does not support a significance claim.

**The honest caveat about drafting.** Speculative decoding produces exactly the
text the large model would have produced alone, so there is no quality tradeoff.
How much faster it is depends entirely on how often the draft guesses right,
which depends on the model pair and on the content being generated. It can be a
large speedup, a small one, or slower than not using it. That is why the Speed
Check exists: the feature is worth having because the answer can now be
measured, not because the measurement is guaranteed to be favourable.

Read throughput, TTFT, drafted tokens, accepted draft tokens, and acceptance
rate together. A higher tokens-per-second result with no drafted tokens, poor
acceptance, or worse TTFT is a trade-off to inspect, not a "best" verdict.
Hermaeus deliberately does not collapse those facts into a magic score.

### First recorded result (r27, 0.34.0-alpha)

`gemma-4-E4B-it-qat-UD-Q4_K_XL` at 64512 context, 999 GPU layers, q8_0 KV
cache, one cold iteration per case, on the maintainer's desktop under ordinary
load. Draft model: `mtp-gemma-4-E4B-it-BF16.gguf`.

| | `ngram-mod` | `draft-mtp` | Delta |
| --- | --- | --- | --- |
| Median tok/s | 69.7 | 70.2 | +0.5 |
| Mean tok/s across the four cases | 69.2 | 70.3 | +1.1 |
| Median time to first token | 4427 ms | 4602 ms | +175 ms |
| Structured output | 69.7 tok/s | 70.2 tok/s | +0.5 |
| Repetitive output | 67.0 tok/s | 70.6 tok/s | +3.6 |
| Code | 70.3 tok/s | 70.2 tok/s | -0.1 |
| Free prose | 69.7 tok/s | 70.2 tok/s | +0.5 |

**Read this as a null result, not a win.** A 1.6% mean difference from one
iteration per case, on a desktop under unknown load, is not distinguishable
from noise, and this document does not claim otherwise. Time to first token got
measurably worse, which is what a second model loading and running ahead of the
first token should do.

The one detail worth keeping: repetitive output, the shape where draft
acceptance should be highest, gained the most and was the only case below
70 tok/s without drafting. That is the direction the theory predicts. It is
also a single sample, and a single sample pointing the right way is a reason to
measure again with more iterations, not a reason to believe it.

If you are reproducing this, confirm in the Services log that the draft model
actually loaded when the server started. Uniform tok/s across content shapes as
different as these is consistent with decode being memory-bandwidth-bound, and
equally consistent with drafting never having engaged at all, in which case
both columns measured the same configuration.

### What r28 changed about repeating it

The entry above stands as written; it is an honest record of what was known
when it was taken. What has changed is that the ambiguity it ends on is now
answerable without reading a server log.

- **Draft acceptance is recorded** (r28 doc 02 2.1 and 2.4). llama-server
	reports `draft_n` and `draft_n_accepted` in its timings whenever
	speculative decoding is active, and a run's results now carry both. A
	result showing `0 drafted` means drafting never engaged and the two columns
	compared the same configuration. A result showing a healthy acceptance rate
	beside a flat tok/s means the bottleneck is somewhere else. A run where the
	server reported no counters at all shows nothing rather than a zero, because
	a missing measurement is not a measured zero.
- **The Speed Check runs five iterations per case** (r28 doc 02 2.2), and a
	comparison reports the median with the range observed across them, in the
	form `70.2 tok/s (66.8 to 71.9 over 5 runs)`. That is a description of what
	was seen. It is not a confidence interval and no significance is claimed.

A rerun is the owner's to do and the owner's to write up, exactly as this one
was. There is no rerun result here and nothing above has been edited to look
better in hindsight.

## System Overview

The **System Overview** page shows the local machine and app environment:

- App version
- Operating system
- CPU information
- RAM (total and available)
- Process memory usage
- Data-root storage usage
- Database footprint breakdown
- Managed component status (llama-server, XTTS, etc.)
- Best-effort GPU/VRAM visibility

### GPU Detection

- NVIDIA systems use `nvidia-smi` when available
- Other GPU probes degrade gracefully with informative messages
- Manual GPU configuration available in Settings and Services

### llama.cpp Tuning and Updates

The Services auto-tune action now probes descending GPU layer candidates and
keeps the highest candidate that starts and reaches `/health`, with CPU fallback
as the final candidate. Successful tune results are saved per GGUF model file
with model size and modified-time metadata. When that model is selected again,
Hermaeus reapplies the saved GPU layer, thread, context, and extra-argument
profile before starting the managed server.

Doctor alerts when local GGUF models do not have matching tuned profiles. It
also checks the configured `llama-server` binary version and, when GitHub
releases are reachable, compares it with the latest `llama.cpp` release. If an
update is available, Doctor can download the matching platform asset, replace
the older managed binary, and update the managed server paths. If the update
check cannot reach GitHub, Doctor reports the local version it could read and
keeps the scan usable offline.

Doctor also verifies the pinned `nomic-embed-text-v1.5` embedding GGUF by
SHA256 when a nomic embedding model is installed. If the file does not match
the pinned artifact, Doctor offers the same verified embedding-model install
action used by first-run setup.

### Storage Tracking

The System Overview displays:

- Total data root size
- Individual database sizes
- Cache footprints
- Model and asset directory sizes

This helps monitor storage usage and plan data cleanup or archival.
