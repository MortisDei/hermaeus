# Dependency Review (2026-07)

Every NuGet reference in `src/`, judged against the project philosophy:
minimise dependencies, prefer owning infrastructure, no lock-in.

## Verdict up front

The dependency posture is already excellent — eight third-party packages plus
Microsoft platform libraries. The correct strategy is **containment, not
elimination**: isolate the two dependencies that could actually hurt (Avalonia,
ONNX Runtime) behind seams you already mostly have, and stop worrying about
the rest.

## Audit

### Avalonia 11.3 (+ Desktop, Themes.Fluent, Fonts.Inter, AvaloniaEdit)

- **Why:** the entire UI. Cross-platform native rendering, XAML, styling.
- **Value:** enormous. It is the only credible way to ship a truly native,
  non-WebView, Linux+Windows desktop app in .NET. This is the load-bearing
  dependency of the whole product thesis ("native desktop, not a WebView
  shell").
- **Baggage:** large surface, its own dispatcher/threading model, styling
  system churn between major versions (10→11 was painful ecosystem-wide),
  Skia underneath, and the project's health depends on AvaloniaUI OÜ's
  commercial viability.
- **Replaceable?** No. WPF sacrifices Linux (fatal to the thesis). MAUI does
  not target Linux desktop. Writing a UI toolkit is a decade of work that
  would kill the product. **Keep, and treat as permanent.**
- **Containment (the realistic hedge):** the codebase already does the right
  thing — ViewModels reference CommunityToolkit.Mvvm only, never Avalonia.
  Enforce that with an architecture test (fail the build if
  `Aether.ViewModels` references `Avalonia.*`). Keep custom controls small and
  few. Pin minor versions; take Avalonia upgrades as deliberate, tested
  events, not routine bumps.
- **AvaloniaEdit** is the one Avalonia satellite worth questioning. It exists
  for syntax-highlighted code display. If usage is read-only rendering (chat
  code blocks, diff previews) rather than editing, a bespoke read-only
  highlighted-text control over your Markdig pipeline would drop a sizeable,
  lightly-maintained dependency. Audit actual usage; if no editing, plan
  removal in 1.x. **Audited.** Exactly one call site
  (`Aether.Desktop/Controls/MarkdownViewer.cs`), read-only (`IsReadOnly =
  true`) chat-markdown fenced-code rendering only, no editing anywhere in the
  app, no TextMate/grammar-registry usage (built-in `HighlightingManager`
  only), and confirmed absent from `Aether.ViewModels` (no layering leak).
  One piece of dead code found and deleted (`ResolveHighlighting`, an unused
  duplicate of the inline `HighlightingManager.GetDefinition` call).
  Decision: keep the dependency rather than replace it. A bespoke read-only
  highlighter would need to reimplement per-language tokenizers for the ~15
  languages `NormalizeFenceLanguage` already maps (C#, Python, JS, Bash,
  SQL, XML, and so on) — that is a real feature reimplementation, not a
  refactor, and disproportionate to what's actually a single well-scoped,
  actively-maintained Avalonia satellite package (unlike the much larger,
  genuinely fragile Python voice stack this doc flags separately). Revisit
  only if AvaloniaEdit itself becomes unmaintained or a real editing use
  case appears. `Aether.Desktop/Controls/DiffView.axaml` renders diffs with
  plain `TextBlock`s, not AvaloniaEdit — that's a deliberate difference
  (line-level add/remove coloring, not language syntax), not an
  inconsistency to fix.

### CommunityToolkit.Mvvm 8.3

- **Why:** `ObservableProperty`/`RelayCommand` source generators.
- **Value:** high, near-zero runtime footprint (source-gen), Microsoft-owned.
- **Baggage:** essentially none.
- **Keep.** Replacing it with hand-written `INotifyPropertyChanged` is
  make-work. Note it leaks into `Aether.Core` — Core should arguably be
  POCO-only; move observable types up to ViewModels over time so Core stays a
  pure contract layer.

### Microsoft.Data.Sqlite 9.x

- **Why:** all local stores (conversations, memory, RAG, task index).
- **Value:** foundational; ships its own SQLite native binary, first-party.
- **Keep.** Non-negotiable. The absence of an ORM is correct — do not add one.

### Microsoft.ML.OnnxRuntime 1.25 + Microsoft.ML.Tokenizers 2.0

- **Why:** cross-encoder reranker inference and tokenization.
- **Value:** the only practical way to run the reranker natively; first-party.
- **Baggage:** the heaviest binary payload in the app (native runtimes per
  platform), version-sensitive to model opsets, meaningful package-size cost.
- **Replaceable?** Not realistically for inference. But it should be
  **optional at runtime, and ideally optional at package time**: the reranker
  is already an explicit Doctor install — consider loading ORT as an
  out-of-band downloaded component too, so the base distribution stays slim
  and users who never rerank never carry it. At minimum keep all ORT types
  confined to the reranker classes (verify: nothing outside
  `Aether.Rag` should import it). Tokenizers is first-party and fine.

### Markdig 0.38

- **Why:** Markdown parsing for native chat rendering.
- **Value:** high; best-in-class, tiny, stable, pure managed code.
- **Keep.** Writing a CommonMark parser is a classic trap. This is exactly the
  kind of dependency the philosophy permits: small, focused, replaceable in
  principle because your rendering layer consumes its AST, not its API surface
  everywhere.

### PdfPig 0.1.x

- **Why:** digital PDF text extraction for RAG ingest.
- **Value:** solves a real problem; pure managed.
- **Baggage:** pre-1.0 versioning, moderately active maintenance, large-ish.
- **Verdict:** keep for now; it touches exactly one ingest loader, so it is
  already contained. If OCR lands later, revisit the whole PDF story once —
  don't accrete PdfPig + an OCR engine + an image decoder independently.

### Tmds.DBus.Protocol

- **Why:** Linux tray/desktop integration (DBus).
- **Value:** necessary for Linux tray; protocol-only (lighter than full Tmds.DBus).
- **Keep.** Small, single-purpose, already the minimal variant.

### System.Numerics.Tensors, Microsoft.Extensions.DependencyInjection

First-party BCL-adjacent. No concerns. SIMD tensor primitives will matter more
as RAG corpora grow — good that it's already in place.

### The Python shadow dependency (unlisted but real)

The voice stack depends on user-machine Python 3.11/3.12, venvs, and
per-provider packages. **This is the largest dependency in the project and it
doesn't appear in any csproj.** It brings version hell, GPU-backend detection,
generated scripts, and a health-validation subsystem that exists solely to
manage it. See Feature Audit: the long-term answer is fewer Python-based voice
providers, favouring ONNX-runnable ones (Kokoro has ONNX exports) so voice
rides the ONNX Runtime you already ship.

## Staged dependency-reduction roadmap

1. **1.0:** No removals. Add architecture tests: ViewModels must not reference
   Avalonia; ONNX Runtime types must not escape `Aether.Rag`; Core must not
   grow package references. Pin Avalonia minor version.
2. **1.x:** ~~Audit AvaloniaEdit usage; replace with an internal read-only
   highlighted-text control if no editing is needed.~~ **DONE**: audited,
   kept (see above — single read-only call site, replacement would be a
   feature reimplementation, not a refactor). Move CommunityToolkit.Mvvm out
   of `Aether.Core` — **DONE**.
3. **1.x–2.0:** Reduce Python surface: adopt ONNX-based Kokoro as the default
   local voice path; demote Python-venv providers (XTTS/F5) to "advanced,
   best-effort" status or a companion add-on.
4. **2.0:** Evaluate shipping ONNX Runtime as a Doctor-installed component
   rather than a bundled package, shrinking base distribution size.
5. **Ongoing:** hard rule — any new package needs a written justification in
   the PR against the philosophy list. The cheapest dependency to remove is
   the one never added.
