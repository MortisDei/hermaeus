# Review round 18: Close out the open work, then agent/model UX and engine options

Written 2026-07-21 against the working tree on top of v0.22.0-alpha
(r17 implemented at 876cc10). Prior rounds live in
`docs/review/archived/`; check each round's roadmap "Explicit
rejections" before proposing anything adjacent.

## Why this round

Three fronts, in strict order.

1. **Finish what's on disk (doc 01).** There are nine files of
   uncommitted changes in the working tree: the r17 headroom/Auto-Tune
   follow-up (`KvCacheMath.cs`, `ModelFitEstimator.cs`,
   `ServerProcessManager.cs`) and an unplanned conversation-title
   auto-save feature (`ConversationItemViewModel.cs`,
   `MainWindowViewModel.cs`, `ConversationListView.axaml`), plus two
   test edits presented as fixes for known flaky tests. Reviewing them
   found: a real UI regression (conversation row timestamps stop
   updating), a save-on-every-keystroke bug that reloads the entire
   conversation list on each keypress, one test "fix" that does not
   fix the test (still fails locally), a behavior change that
   contradicts its own doc comment, and no doc/changelog updates for
   any of it. None of this ships until doc 01 is done.
2. **User-facing friction (docs 02-03).** Three items came directly
   from using the app: the built-in Agent Scenarios suite failed 3 of
   its checks in a live run and the Agents view buries the one thing
   the user is looking at (the agent's current response) in an
   11px truncated header label while a full response preview sits
   scrolled off-screen in a collapsed expander (doc 02); the Hugging
   Face search results list clips instead of scrolling, sharded GGUF
   downloads each surface as a separate model-list row, and the chat
   view renders every recalled memory as an always-visible pill instead
   of a collapsed count (doc 03).
3. **Engine options (doc 04).** An owner-supplied llama-server tuning
   guide surfaced how much of the engine's memory and throughput
   surface Aether hides behind free-text ExtraArgs: KV cache
   quantization (q8_0/q4_0 halves-to-quarters the context cost the
   r17 fit math computes), flash attention, rolling context shift,
   mlock/no-mmap. These become first-class, opt-in server options
   with defaults byte-identical to today; the app recommends via an
   explicit preset button and never forces a value.

## Reading order

- `01-finish-the-open-work.md` - fix the uncommitted diff before
  building anything new
- `02-agents-usability.md` - scenario suite correctness, Agents view
  layout
- `03-model-catalog-and-memory-ui.md` - HF browser scrolling, shard
  dedupe, chat memory disclosure
- `04-llama-server-engine-options.md` - first-class engine flags, KV
  cache type in the fit math, hardware-tier preset helper
- `05-roadmap.md` - ship shape, sequencing, test expectations,
  rejections

## Ground rules (unchanged from prior rounds)

- Zero-warning build; all tests green before finishing (verified: the
  working tree currently builds with 0 warnings but has 1 failing
  test - see doc 01).
- No em dashes anywhere.
- No new NuGet packages.
- Additive JSON/schema only; stored runs and profiles must keep
  loading.
- Warnings inform, the user decides: nothing changes a server config,
  context size, model selection, or file on disk without an explicit
  user action (Auto Tune click, Save, Delete).
