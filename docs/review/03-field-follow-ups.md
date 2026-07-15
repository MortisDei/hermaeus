# 03 - Field follow-ups from 0.14.0-alpha

Three issues from the owner's first day on 0.14.0-alpha. The r9 wins
held (no crashes, first-click Start, faster sends); these are the
residuals.

## 3.1 Unhandled exception on shutdown

Field stack: `System.InvalidOperationException: 'Aether.Mcp.
McpToolBridge' type only implements IAsyncDisposable. Use DisposeAsync
to dispose the container.` raised from
`ServiceProviderEngineScope.Dispose()` inside `Window.HandleClosed`.

Cause: `App.OnFrameworkInitializationCompleted` wires
`desktop.Exit += ... finally { sp.Dispose(); }`
(`src/Aether.Desktop/App.axaml.cs:48-64`). `McpToolBridge` is
`IAsyncDisposable`-only (`src/Aether.Mcp/McpToolBridge.cs:7,87-98`),
and `ServiceProvider.Dispose()` throws for any registered singleton
that cannot be disposed synchronously. The exception escapes the
window-close handler and reaches the top level AFTER the `try/catch`
above it, because it is thrown in the `finally`.

Fix:

- Replace `sp.Dispose()` with a synchronous wait on the async path:
  `sp.DisposeAsync().AsTask().GetAwaiter().GetResult();` wrapped in
  its own try/catch that logs to stderr. Blocking here is acceptable:
  the app is exiting, there is no UI thread work left to deadlock on,
  and `McpToolBridge.DisposeAsync` already swallows per-session
  errors. Bound it: `Task.WaitAll` with a 5-second timeout (or
  `.Wait(TimeSpan.FromSeconds(5))`) so a hung MCP server process
  cannot wedge shutdown; on timeout, write one stderr line and let the
  process exit (the r9 job object reaps any child the dispose did not
  reach).
- Guard the class of bug, not just the instance: add a test that
  builds the full Composition + Desktop service collection (the same
  registrations `App.ConfigureServices` uses; extract if needed),
  resolves nothing, and calls `DisposeAsync()` on the provider, then
  separately asserts via reflection that every singleton registration
  whose implementation type implements `IAsyncDisposable` but not
  `IDisposable` is documented in the test (a list that must be
  consciously extended), so the next async-only service does not
  reintroduce a sync-dispose crash path elsewhere.

Acceptance criteria:

- App exits cleanly with an MCP server configured and sessions open
  (manual verification step, plus the container test above).
- No `sp.Dispose()` call remains on the exit path.

## 3.2 20-30 s of silence before prompt processing starts

Field report: sends are much faster than the pre-r9 minute-plus, but
each response still starts with roughly 20-30 seconds where nothing
audible happens and the GPU fans only then spin up (prompt eval
starting late). The r9 `ChatSendTiming` line (`recall X ms, select X
ms, lesson X ms, prompt build X ms, first token X ms`) is persisted on
every chat trace (`src/Aether.ViewModels/ChatViewModel.cs:443,495`)
and shown in the trace panel; it already answers WHERE the time goes
up to the HTTP request. What it cannot see is inside the server: a
large `FirstTokenMs` does not distinguish request queueing, prompt
re-tokenization, KV-cache reprocessing after context shift, or genuine
prompt eval on a huge `--ctx-size`.

Work, in order:

1. **Read the numbers first.** Before writing any fix, pull the
   `ChatSendTiming` breakdown from the owner's recent traces (trace
   panel or `traces.db`) and record them in the implementation notes.
   If `FirstTokenMs` is not the dominant stage, stop and reassess this
   item against what actually dominates; the sub-items below assume
   the wait is server-side.
2. **Parse llama-server's `timings` object.** llama.cpp's final
   streamed chunk (and non-streamed responses) includes `timings`
   with `prompt_n`, `prompt_ms`, `predicted_n`, `predicted_ms`. In the
   llama.cpp chat client, capture it when present and carry it onto
   the chat trace + `ChatSendTiming.Format()` output as
   `server prompt N tok / X ms`. This decomposes `FirstTokenMs` into
   "server was evaluating the prompt" vs "request waited before
   evaluation". Providers that do not send timings simply leave the
   fields null; no per-provider special cases beyond llama.cpp.
3. **Slow-send visibility.** When the pre-first-token portion of a
   send exceeds 10 s, write one `PerformanceLog` WARNING with the full
   breakdown (including server timings when available). Today the
   line is Info-level and only visible if the owner goes looking.
4. **Prompt-cache check, evidence permitting.** A likely cause of
   fans-late behaviour with a 64k context is the full conversation
   being re-evaluated every send (no prompt caching, or cache
   invalidated by a changing prefix such as injected memory/lesson
   blocks that reorder between sends). If step 2 shows `prompt_n` in
   the tens of thousands on every send: (a) verify the system-prompt
   assembly is byte-stable between consecutive sends when memories and
   lessons have not changed (stable ordering, no timestamps), and fix
   ordering if not; (b) surface a Doctor advisory when the managed
   server launch args lack prompt caching (llama-server caches by
   default; `--no-context-shift`/`cache_prompt` interactions are
   version-specific, so the advisory should quote the observed
   `prompt_n` evidence rather than guess). Do not auto-change server
   flags (r9 rejection of speculative context tuning stands).

Acceptance criteria:

- `timings` parsing has a unit test over a canned final-chunk payload
  and tolerates its absence.
- The WARNING fires in a test with a fabricated slow timing and does
  not fire for a fast one.
- Implementation notes record the owner's actual stage numbers and
  which hypothesis they support (comparable to r9's
  measurement-before-tuning rule).

## 3.3 Voice: dictionary misses gain a spoken trailing "e"; typographic punctuation garbles words

Field report: "Joke" speaks as "Jok-e" while "joke" is correct; some
words gain a random trailing "e"; em dashes (U+2014) confuse the
phonemizer badly.

Diagnosis from code:

- **Inverted Magic-E check.** The letter-rule fallback treats a final
  "e" as silent only when the PRECEDING character is a vowel
  (`src/Aether.Voice/KokoroPhonemizer.cs:218`:
  `word[^1] == 'e' && "aeiouy".Contains(word[^2])`). The actual
  English pattern is vowel-CONSONANT-e ("joke", "hope", "state"), so
  for exactly those words the fallback speaks the final "e" as a full
  vowel: the reported trailing-"e" sound. Any token that misses the
  dictionary and ends consonant+e exhibits it.
- **Why capitalized words miss the dictionary at all.** Lookup
  lowercases first (`KokoroPhonemizer.cs:63`), so bare "Joke" and
  "joke" resolve identically; the misses come from characters glued to
  the word. `AppendWord` strips only sentence punctuation
  (`.,!?;:`) and straight quotes/parens (`:42-48`). Typographic
  characters that LLM chat output produces constantly are NOT
  stripped: curly quotes (U+2018/U+2019/U+201C/U+201D), em/en dashes
  (U+2014/U+2013) glued to words or standing alone, ellipsis (U+2026),
  and markdown residue that survives sanitization (`*`, `` ` ``, `_`).
  A token like [U+201C]Joke[U+201D] misses the dictionary, fails the
  acronym check, and lands in the letter fallback where the quote
  chars map to nothing and the broken Magic-E rule adds the "e". This
  also explains capitalization correlating with the bug: capitalized
  words begin sentences and quoted spans, where these characters
  attach.
- **Em dashes.** Kokoro's vocab has no U+2014 token, so a standalone
  dash is silently dropped, and "word[U+2014]word" without spaces
  stays one token, misses the dictionary, and letter-spells both words
  fused.

Fix, in `KokoroTextNormalizer.Normalize` (so every consumer benefits
and the phonemizer's per-word logic stays simple):

1. Map U+2014, U+2013, and "--" surrounded by word characters or
   spaces to ", " (comma + space): produces the natural pause the
   punctuation implies and splits fused words.
2. Map curly single/double quotes to their straight equivalents,
   U+2026 to "...", and strip stray markdown emphasis characters
   (`*`, `` ` ``, `_`) that are not inside a word.
3. In `KokoroPhonemizer.AppendWord`, extend the trim set with straight
   quotes on BOTH sides (leading quotes are currently untouched) and
   square brackets.
4. Fix the Magic-E rule to the real pattern: final "e", preceded by a
   consonant, with a vowel somewhere before that consonant
   ("joke" yes, "see" no, "the" no: length > 3 or vowel check makes
   "the"/"she" fall through to their dictionary entries anyway, which
   always win; the rule only governs the fallback tier).
5. Diagnosability: log (Debug level, once per distinct word per
   session) every word that falls through to the letter fallback, so
   the next pronunciation report can be checked against actual
   fallback words instead of guessed at.

Acceptance criteria:

- Golden tests: "Joke" (bare, capitalized), [U+201C]Joke[U+201D] with
  curly quotes, "state" / "hope" / "joke" via a forced fallback path
  (test the private rule through a word absent from cmudict, e.g.
  "zoke" -> no trailing vowel), "wait" + U+2014 + "what" normalizing
  to a comma pause, "**Bold** word" markdown residue, U+2026.
- Existing 24 golden-sentence tests stay green.
- The normalizer never emits characters outside Kokoro's vocab for
  the covered set (assert via `KokoroTokenizer.Encode` round-trip on
  the test sentences: no silently dropped characters).
