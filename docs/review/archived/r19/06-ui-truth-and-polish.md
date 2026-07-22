# 06. UI truth and polish

## 6.1 Memory pills must show the memory

Owner: "in Memories view, a user has no way to see what the memory is, just a bunch of
meaningless keywords". Verified: the Memories PAGE renders full content
(`MemoriesView.axaml:122-124`), but the chat-side "Memories used: N" pills added in r18
show only `SourceReference.Title` (`MessageControl.axaml:102`), which is a keyword
extraction (`MemoryExtractionService.TitleFrom`), with the actual content hidden behind
a hover tooltip. That matches the complaint; fix the pills first, then verify the
Memories page against the owner's reading with a screenshot if the complaint persists.

- Expanded pill click: replace the tooltip-only design with a Flyout (or inline
  expander under the pill row) showing the full memory content (`Snippet` already
  carries it, `ConversationMemoryService.cs:336`), category, and a "Open in Memories"
  link that navigates to the Memories panel with the search box prefilled with the
  memory title (navigation via the existing panel-switch command on
  `MainWindowViewModel`; prefill via a parameter on `MemoriesViewModel`).
- Keep the collapsed count pill exactly as r18 shipped it.

Acceptance: VM-level test that the flyout content binds the snippet, and the navigate
command sets the Memories search text; manual check of the visual.

## 6.2 System Overview: hardware above the privacy audit

Owner: GPU/VRAM and Components are buried under the privacy audit. Verified in
`src/Aether.Desktop/Views/SystemOverviewView.axaml`: the Privacy Audit expander sits at
:48, GPU / VRAM at :97, Components at :123.

Fix: pure XAML reorder: system info sections (CPU/RAM summary if present, GPU / VRAM,
Components) first, Privacy Audit expander after them (keep it `IsExpanded="True"`; it
loses nothing by being lower). No VM changes.

Acceptance: manual screenshot; no test.

## 6.3 Chat must actually scroll to the bottom (action row cut off)

Owner: the window "does not scroll down far enough to see the copy and voice buttons".
Verified mechanism: `ChatView.axaml.cs:48-56` scrolls to `Extent.Height` when the VM
raises `ScrollToBottom`, at `DispatcherPriority.Background`. But content grows AFTER
the last raise: `MarkdownViewer` re-renders on a timer (`OnRenderTimerTick`), and the
final layout (action row, sources, memory pills) materializes when streaming completes,
so the last programmatic scroll always lands short of the true final extent.

Fix (standard stick-to-bottom): track pinned state in `ChatView`:
- Pinned = user is within ~40 px of the bottom; recompute on user-initiated
  `ScrollChanged` (offset moves away from bottom -> unpinned; back to bottom ->
  pinned).
- Subscribe to the ScrollViewer's extent changes (`ScrollChanged` where
  `ExtentDelta != 0`, or `MessagesList.LayoutUpdated`): while pinned, snap offset to
  the new extent. The VM's `ScrollToBottom` event now just sets pinned = true and
  snaps once (sending a message re-pins even if the user had scrolled up).
- This keeps the user's position when they scroll up mid-stream (do not fight the
  user), which the current code already accidentally does; preserve it.

Acceptance: manual: stream a long code-heavy response, end state shows the action row
without touching the wheel; scrolling up mid-stream is not yanked back down.

## 6.4 Thinking indicator with changing status words

Owner wants Claude-Code-style rotating whimsy while the model is thinking (the blank
bubble before first token). r14 already added phase feedback structure to the blank
bubble; extend it:

- A static word list in `ChatViewModel` (e.g. "Thinking", "Pondering", "Herding
  tokens", "Warming the cache", "Consulting the weights", "Brewing", "Untangling",
  "Sharpening pencils"): pick randomly every 2.5 s via a `DispatcherTimer`-free
  approach (VM-side `Task.Delay` loop guarded by `IsGenerating`, marshaled with
  `RunOnUi`) and expose `ThinkingLabel` with trailing elapsed seconds: "Pondering...
  12s". Stop rotating when the first content token arrives.
- Keep it honest: while the r14 phase info knows something concrete ("processing
  prompt, 9,744 tokens"), show that instead of whimsy; whimsy only fills the unknown
  gaps. Respect the existing `thinking-pulse` style in `ChatView.axaml:21`.

Acceptance: VM test: label rotates over time while generating and freezes/clears on
first token; phase text wins over whimsy when present.

## 6.5 Message bubbles: borders to separate user and assistant

Owner wants visible separation. `MessageControl.axaml` renders both roles with the
same flat layout. Fix in XAML only:
- User messages: a `Border` with `CornerRadius="10"`, subtle background
  (`SubtleFillColorSecondaryBrush`), right-aligned max-width ~70%.
- Assistant messages: full-width `Border` with a 1px `ControlStrokeColorDefaultBrush`
  border, transparent background, left accent optional. Keep the copy/speak action
  row OUTSIDE the border so 6.3's math stays simple. Role-conditional styling via the
  existing `IsUser`-style binding the control already uses for alignment (verify the
  property name in `MessageViewModel`).

Acceptance: manual screenshot in light and dark theme; no test.

## 6.6 Benchmark rankings that actually rank

Owner: "the rankings reveal nothing except a percentage, stuff is cut off". Verified
in `BenchmarkView.axaml:172-216`: a 6-column grid (Model, Runs, Score, Pass Rate,
Speed, info button) where Score/PassRate are bare numbers, the info button is a
32px-wide `Content="i"` that clips, and nothing explains what Score means or which row
is best.

Redesign the Rankings tab (XAML + small VM additions, no scoring math changes):
- Add a rank column: 1, 2, 3... in `RankedRuns` order (expose `Rank` on
  `BenchmarkRunViewModel` or wrap in an indexed projection).
- Score becomes a labeled bar: a horizontal fill proportional to score with the
  number inside, plus a one-line caption under the header row: "Score = quality on
  the suite's checks (0-100). Speed and pass rate are shown separately." Use the
  existing tokens/sec and pass-rate values as plain columns with units.
- Fix the clipping: give the last column a fixed `MinWidth`, replace the "i" button
  with a "Details" text button, and set `MinWidth` on the numeric columns so nothing
  ellipsizes at default window width. The whole grid gets `ScrollViewer`
  `HorizontalScrollBarVisibility="Auto"` as a floor.
- Row details: `ShowRunInfoCommand` already exists; make the row itself clickable
  (same command) so discovery does not depend on the small button.
- Empty/insufficient state: when fewer than 2 ranked models exist, show "Run the same
  suite against two or more models to compare them" instead of a lonely row.

Acceptance: VM test for the rank projection ordering; manual screenshot showing no
clipped columns at the default window size.
