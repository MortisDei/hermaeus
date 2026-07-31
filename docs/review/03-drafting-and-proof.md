# 03. Drafting, and proof that it helped

## Why this is possible now

Draft-model speculative decoding has been deferred since r18 4.4. The
recorded reason: it needs a second model file, a second VRAM budget, and a
picker whose wrong answer silently costs performance instead of failing
visibly.

Two of those three have dissolved.

`unsloth`'s Gemma 4 repositories ship a **Multi-Token Prediction head**
beside the model, and the owner already has them downloaded and unused:

```
.../models--unsloth--gemma-4-E4B-it-qat-GGUF/snapshots/e4a9ed.../MTP/
    mtp-gemma-4-E4B-it-BF16.gguf          171 MB
.../models--unsloth--gemma-4-E4B-it-qat-GGUF/snapshots/bbcd9d.../
    mtp-gemma-4-E4B-it.gguf                59 MB
.../models--unsloth--gemma-4-12B-it-qat-GGUF/snapshots/7102bd.../
    mtp-gemma-4-12B-it.gguf               253 MB
```

An MTP head is trained as part of its base model and shares its vocabulary
by construction, so the compatibility question that made the picker
dangerous does not arise for this class of draft. And 59 MB drafting for a
4.2 GB model is the size ratio speculative decoding actually wants; a draft
that is half the size of the target (Gemma E2B against E4B, say) usually
loses to the verification overhead and is the wrong thing to reach for.

The installed `llama-server` (b10195) exposes `draft-mtp` as a first-class
`--spec-type`.

## What speculative decoding does, in one paragraph

A small cheap model proposes the next few tokens. The large model verifies
all of them in one forward pass instead of one pass per token. Where the
proposal was right, those tokens are kept for roughly the cost of a single
decode step; where it diverges, the tail is discarded. The text produced is
the text the large model would have produced alone. It is a speed
optimisation with no quality tradeoff, and its benefit is entirely a
function of how often the draft guesses right, which is why it must be
measured rather than assumed.

## 3.1 One composable speculative-decoding section

`ServerConfig.NgramSpeculative` is a `bool` (`ServerConfig.cs:53`) that
emits a single value into a flag that is a list:

```csharp
if (cfg.NgramSpeculative && !HasArg("--spec-type"))
{
    parts.Add("--spec-type");
    parts.Add("ngram-mod");
}
```

`--spec-type` accepts a comma-separated list, and drafting and n-gram
speculation are not mutually exclusive. A second independent bool beside the
first would give two knobs that both own one flag and can contradict each
other, which is how this area acquires a bug that only appears in one
configuration.

So the bool is replaced by one section on `ServerConfig`:

```csharp
public sealed class SpeculativeDecodingConfig
{
    public List<string> Types  { get; set; } = [];   // e.g. ["ngram-mod"], ["draft-mtp"]
    public string DraftModelPath { get; set; } = string.Empty;
    public int?    DraftGpuLayers { get; set; }
    public int?    NMax { get; set; }
    public int?    NMin { get; set; }
    public double? PMin { get; set; }
}
```

**Migration is not optional and must not lose the owner's setting.** Both of
the owner's managed servers currently have `NgramSpeculative: true` in a
live `settings.json`. `SettingsService.NormalizeManagedServers` (already the
home for this kind of read-time repair, called from
`ServicesViewModel.cs:1219`) upgrades a legacy `true` to
`Types = ["ngram-mod"]` exactly once. The old property is read and never
written again. A settings file written by 0.33.0 must produce byte-identical
launch arguments after the upgrade, and there is a test for precisely that.

Per the placement rule in CLAUDE.md this is process and runtime
configuration, so it belongs on the **Services** page, replacing the single
checkbox at `ServicesView.axaml:447`.

## 3.2 The launch flags, as the installed binary actually names them

Read from `C:\AI\llama-server\b10195\llama-server.EXE --help` directly.
**Re-read it before implementing**; this surface is being renamed upstream
and the pack may be stale by the time it is built.

| Setting | Flag | Notes |
| --- | --- | --- |
| Types | `--spec-type <a,b>` | `none, draft-simple, draft-eagle3, draft-mtp, draft-dflash, draft-dspark, ngram-simple, ngram-map-k, ngram-map-k4v, ngram-mod, ngram-cache`. Comma-separated list. Default `none`. |
| Draft model | `--spec-draft-model` (`-md`, `--model-draft`) | Default unused |
| Draft GPU layers | `-ngld` (`--gpu-layers-draft`) | |
| Max drafted tokens | `--spec-draft-n-max N` | Default 3 |
| Min drafted tokens | `--spec-draft-n-min N` | Default 0 |
| Min draft probability | `--spec-draft-p-min P` (`--draft-p-min`) | Default 0.00 |

**Removed flags, which will silently do nothing if emitted:**

```
--draft, --draft-n, --draft-max     "the argument has been removed"
--draft-min, --draft-n-min          "the argument has been removed"
--spec-ngram-size-n / -size-m / --spec-ngram-min-hits
```

This is the single most likely way for this document to be implemented
wrongly: an agent writing from prior knowledge of llama.cpp emits
`--draft-max`, the server starts fine, nothing changes, and the feature
appears to work. Emit only flags the installed binary lists, and add a test
asserting no removed flag name is ever produced.

The existing `HasArg` escape hatch, where `ExtraArgs` always wins over a
first-class option (`ServerProcessManager.cs:550-553`), applies here
unchanged.

## 3.3 An incompatible draft model is refused before launch

This is the part r18 was right to be afraid of, and it is the only part that
needs judgement.

Before a server with `draft-*` in its `Types` is started:

- The draft model path goes through **the same validation `ModelPath`
  already gets**: existence, path-traversal rejection, symlink rejection.
  Nothing new is invented here; reuse the existing checks.
- The draft file's GGUF metadata is read and its vocabulary size and model
  family compared with the target's. A mismatch **refuses the start** with a
  message naming both models and both vocabulary sizes, in the same shape as
  the existing port-conflict refusal (`ServerProcessManager.cs:55-65`), which
  is the precedent for failing fast with the cause named rather than
  launching something doomed.
- A draft model larger than half the target's file size produces a warning,
  not a refusal. It is a bad idea rather than a broken one, and the speed
  check in 3.5 will show it.

If reading GGUF metadata turns out to need more machinery than a header
parse, stop and reduce scope to the file-level checks plus the warning: the
MTP case, which is the one the owner will actually run, is compatible by
construction. Do not add a GGUF parsing library. Do not add any package.

## 3.4 The second VRAM budget is visible before it is spent

A draft model is a second allocation. `ModelFitEstimator` and the existing
GPU/VRAM reporting in System Overview already know how to talk about this.
The Services page shows the combined estimate for target plus draft before
the server is started, using the estimator that exists.

If the combined estimate exceeds available VRAM this is surfaced as
information, not a block. The user may have reasons, and llama.cpp will
spill to system memory rather than fail.

## 3.5 A speed check that records what produced the number

Enabling drafting without a way to measure it produces a knob that is
believed rather than known, on hardware where the answer genuinely varies.

`BenchmarkService` already measures the right things, from llama-server's
own `timings` rather than by estimating: `ApproxTokensPerSecond` is
`predicted_n / predicted_ms` (`BenchmarkService.cs:830-851`),
`PromptTokensPerSecond` is the prompt-side equivalent (`:853`), and
`FirstTokenMs` is recorded per result. `BenchmarkRunMetadata`
(`BenchmarkMetadata.cs`) already captures `ModelPath`, `ContextSize`,
`GpuLayers`, `Threads`, `BatchSize`, `Backend` and `RuntimeVersion`.

So the speed check is composition, not new measurement:

- A built-in suite alongside `StarterSuites()` (`BenchmarkService.cs:618`),
  fixed and short: a handful of prompts with deterministic settings, chosen
  to include the shapes where drafting behaves differently (repetitive and
  structured output, where acceptance is high; free prose, where it is not).
  It is a speed measurement, so its cases assert nothing about quality.
- `BenchmarkRunMetadata` gains the speculative-decoding settings, so a run
  records the configuration that produced it. Without this the comparison in
  3.6 has nothing to key on.
- It runs against the **currently configured server**, from the Services
  page, next to the settings it measures.

## 3.6 The comparison

Two speed-check runs of the same suite against the same model, with
different speculative settings, show a comparison: tokens per second, prompt
tokens per second, and time to first token, with the configuration
difference that separates them.

Deliberately absent, and these are not oversights:

- **No verdict, grade, score or recommendation.** The app reports what
  happened. Settled by r23 2.3 and unchanged.
- **No automatic tuning, sweep, or "find the best settings" button.** A
  sweep is a benchmark suite designer in disguise, rejected in r25 and again
  in r26.
- **No statistical significance claim.** A handful of runs on a desktop
  under unknown load does not support one, and printing a confidence
  interval would imply a rigour the measurement does not have.

## 3.7 Doctor and docs

Doctor gains one check, in the shape its existing checks use: a server
configured with a `draft-*` type but no draft model path, or a draft path
that no longer exists on disk. Both are configurations that will fail at
start, and Doctor's job is to say so before the user finds out by starting.

`docs/benchmarks.md` documents the speed check. `docs/features.md` and
`README.md` get the capability. Do not describe drafting as a speedup
without saying that the size of the speedup depends on the model pair and
the content, because on this feature that caveat is the honest part.

## Tests

| Area | Test |
| --- | --- |
| 3.1 | A legacy `NgramSpeculative: true` config upgrades to `Types = ["ngram-mod"]` exactly once |
| 3.1 | A 0.33.0 settings file produces byte-identical launch arguments after upgrade |
| 3.1 | An already-upgraded config is not re-upgraded or duplicated |
| 3.2 | `Types` of two values emits one comma-separated `--spec-type` |
| 3.2 | Empty `Types` emits no `--spec-type` at all |
| 3.2 | No removed flag name (`--draft-max`, `--draft-min`, `--draft-n`, `--spec-ngram-size-n`) is ever emitted |
| 3.2 | `ExtraArgs` containing `--spec-type` still suppresses the generated one |
| 3.2 | Draft settings emit nothing when no `draft-*` type is selected |
| 3.3 | A draft path that does not exist refuses the start and names the path |
| 3.3 | A draft path outside the allowed roots is rejected by the existing path validation |
| 3.3 | A vocabulary-size mismatch refuses and names both sizes |
| 3.3 | A draft larger than half the target warns but starts |
| 3.5 | The speed-check suite is well-formed and its cases carry no quality assertions |
| 3.5 | Run metadata round-trips the speculative settings |
| 3.6 | Comparison pairs two runs by model and suite and reports the config delta |
| 3.6 | Comparison refuses to compare runs of different models or suites |
| 3.7 | Doctor flags a `draft-*` type with a missing or empty draft path |

Argument-building tests are pure functions over `ServerConfig` and are the
bulk of this. Nothing here needs to launch a server. The one thing tests
cannot prove is that drafting is faster on this hardware, which is what 3.5
exists to let the owner find out.

## Verification the owner must do by hand

Tests cannot confirm the feature works, only that the right flags are built.
Before this document is called done:

1. Point the Chat server's draft model at
   `.../MTP/mtp-gemma-4-E4B-it-BF16.gguf`, set `Types` to `["draft-mtp"]`.
2. Start it. Confirm it reaches healthy and the log shows the draft model
   loading.
3. Run the speed check. Record tok/s.
4. Set `Types` back to `["ngram-mod"]`, restart, run the speed check again.
5. Record both numbers in the PR body.

If drafting is slower, that is a legitimate result and it goes in the PR and
in `docs/benchmarks.md`. This feature is worth having because it can now be
measured, not because the measurement is guaranteed to be favourable.

## What this doc explicitly does not do

- **No draft model downloading, or a draft-model catalogue.** Doc 04 makes
  companions arrive with their model, which is the general fix. A
  drafting-specific downloader would be a second one.
- **No automatic selection of a draft model from a filename convention.**
  Considered and declined: firing a runtime configuration change off the
  presence of an `MTP/` folder is magic that is hard to explain when it
  guesses wrong. Doc 04 makes the file present and adjacent; the user points
  at it once.
- **No `eagle3`, `dflash` or `dspark` support.** They are in the
  `--spec-type` list and they are out of scope. `Types` is a list of
  strings, so supporting them later is data, not code.
- **No speculative decoding for the embedding server.** Embeddings do not
  decode.
- **No change to the risk classification, the approval gate, or anything in
  `Hermaeus.Agent`.** This round does not touch them.
