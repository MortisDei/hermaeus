# 04. Workspace editor decision

## 4.1 Decision and evidence

**P:** investigate Monaco first for a dedicated Workspace editor/diff surface,
but retain AvaloniaEdit as the functional fallback. Run one bounded comparison
after the authority/receipt contract is fixed. Do not make Monaco a prerequisite
for Agent correctness or an excuse for an IDE project.

**RF:** Desktop already references `Avalonia.AvaloniaEdit` 12.0.0 alongside
Avalonia 12.1.2. Agent Workspace currently presents a file preview and proposed
content textbox (`src/Hermaeus.Desktop/Views/AgentView.axaml:1059-1110`).
`MarkdownViewer.cs:515` records an earlier blank-text failure from realizing an
AvaloniaEdit TextView in a virtualized, unbounded chat layout. That is evidence
against that embedding pattern, not proof a bounded primary editor cannot work.

## 4.2 Current primary-source research

Checked 2026-09-07. Package metadata reported Avalonia.Controls.WebView 12.1.0
as latest stable, released 2026-08-15. These facts are upstream claims, not local
rendering proof.

| Option | Verified facts | Decision |
| --- | --- | --- |
| Monaco + Avalonia.Controls.WebView | Monaco is MIT; current WebView source is MIT too. WebView documents Windows WebView2 and Linux WPE/WebKitGTK. | Leading rich-editor spike. Additional NuGet/browser assets must earn their size, maintenance and attack surface. |
| Existing AvaloniaEdit | MIT, already a dependency, native Avalonia text editor. A two-pane diff can reuse Hermaeus patch diff data. | Baseline/fallback. Measure dedicated editor realization instead of repeating the chat-layout failure. |
| CefGlue/CEF | Maintainer supports Avalonia on Windows and Linux x64; wrapper is MIT; native Chromium distribution carries its own notices and update burden. | Reject as default for R33. Revisit only if native WebView cannot meet mandatory behavior and Monaco's benefit justifies an embedded browser distribution. |
| Browser dialog/external browser | Does not provide the primary in-workbench source inspection target requested here. | Reject as the normal Workspace editor. No network-hosted editor. |

Primary references:

- [Monaco README at inspected revision](https://github.com/microsoft/monaco-editor/blob/d620ca0c03d24a51c05ae4dca8a9d5923a4aeb9c/README.md)
  and [MIT license](https://github.com/microsoft/monaco-editor/blob/d620ca0c03d24a51c05ae4dca8a9d5923a4aeb9c/LICENSE.txt).
  Monaco models have URI identity, language services use workers, AMD packaging
  is deprecated, and VS Code extensions do not simply run in Monaco.
- [WebView MIT license at inspected revision](https://github.com/AvaloniaUI/Avalonia.Controls.WebView/blob/b45e042d21d96371bb6d07822a55c85ee5f74d2f/LICENSE),
  [12.1.0 release](https://github.com/AvaloniaUI/Avalonia.Controls.WebView/releases/tag/12.1.0),
  [control API](https://docs.avaloniaui.net/controls/web/nativewebview).
- [Embedding and Linux prerequisites](https://docs.avaloniaui.net/docs/app-development/embedding-web-content):
  WPE offers offscreen SHM rendering; WebKitGTK needs X11/XWayland. Ubuntu lacks
  the listed WPE package, so Pop!_OS must not be assumed to have the WPE path.
  The page still contains an earlier WPE-only note alongside newer fallback
  documentation. Test the selected package/backend rather than resolving this
  inconsistency by assertion.
- [WebView environment controls](https://docs.avaloniaui.net/controls/web/webview-environment)
  and [Avalonia Linux backend](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).
  COSMIC Wayland session, Avalonia native Wayland, Avalonia X11/XWayland and
  browser backend are four distinct facts. Current Hermaeus uses
  `UsePlatformDetect` plus X11 WM class configuration, not an explicit native
  Wayland opt-in.
- [WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution):
  offline installer and Fixed Version options exist; Fixed Version transfers
  browser updates to the app distributor and adds over 250 MB. Use installed
  Evergreen where suitable, with explicit missing-runtime behavior. Do not
  silently fetch a runtime when opening Workspace.
- [AvaloniaEdit source/license](https://github.com/AvaloniaUI/AvaloniaEdit/tree/be976eacf40ed3c6992ca3157773e8f1e7315eae),
  [CefGlue platform/license matrix](https://github.com/OutSystems/CefGlue).

MIT components do not change Hermaeus's PolyForm Noncommercial licensing.
Include exact asset/component notices and verify selected native runtime
redistribution terms before packaging. No paid component is selected here.

## 4.3 Narrow bridge and offline assets

**Outcome:** Hermaeus owns authority; the editor owns presentation and unsaved
text. The same application command validates and applies changes from either
editor. Web content is an untrusted client even when bundled locally.

**P bridge:** versioned messages for document id/version, text, language,
read-only state, bounded edit intent, original/modified diff, reveal range,
selection/cursor, and diagnostics. Include task/workspace view generation and
reject stale, unknown, oversized and malformed messages. Use opaque document
ids, not file URLs revealing paths. No general reflection, host-object export,
script evaluation endpoint or command dispatch by arbitrary name.

Host sends text as serialized data, never as interpolated JavaScript or HTML.
The editor cannot read arbitrary files, write files directly, approve, run
processes, access Git/SQLite/MCP or choose a provider. Save requests produce
Hermaeus proposals and review. Read-only diff views cannot mutate through a
secondary bridge route. Editor failure leaves unsaved text recoverable and
cannot kill an operation or change task status.

Bundle a pinned ESM build, language workers and required fonts/assets locally;
record hashes/licenses in the package. Build-time tooling can be pinned; runtime
npm/CDN access is forbidden. Monaco's documented file:// worker limitation means
simply loading a local HTML file is not a sufficient offline design.

Prefer a host-owned virtual origin/resource handler if the selected adapters
support workers and identical restrictions on both platforms. Otherwise evaluate
an isolated loopback asset server serving only exact bundled assets, with a
random port, no workspace files, no mutation routes, bounded requests and host
lifetime. This is an asset transport, not the Local API or an application server.
The spike must prove worker URLs, CSP and interception on both engines; neither
transport is approved merely because its API exists.

Block external navigation, subresource traffic, popups, downloads and arbitrary
schemes. Deny camera/microphone/geolocation permissions. Avoid persistent browser
profiles containing source text; define cache cleanup and crash retention.
Restrict clipboard behavior to explicit user actions. CSP is defense in depth,
not proof of no network. Capture attempted requests in offline verification.

## 4.4 Spike gate and comparison protocol

**Boundary:** disposable implementation spike after planning approval, one
primary Workspace pane, no production default change until the gate passes.
One new package needs written justification in the eventual PR.
**Dependencies:** doc 03 proposal/authority contract and doc 08 artifact job.

Run the same representative small, large and long-line files and two-pane diffs
in Monaco and AvaloniaEdit. Measure cold open, edit/diff latency, process RAM,
GPU/whole-device pressure, package bytes and child cleanup. Record exact sizes
and timing methodology. Resource totals from a browser are not model VRAM.

| Required case | Windows 11 | Pop!_OS/COSMIC |
| --- | --- | --- |
| Text/diff renders, editable/read-only contract | WebView2 and AvaloniaEdit | WebKitGTK/XWayland and AvaloniaEdit; WPE only if available |
| Offline cold start, workers, no external traffic | Real installed runtime | Real installed native libraries |
| Selection, keyboard shortcuts, IME, copy, accessibility, DPI, focus | Owner live | Owner live |
| Resize, tabs, scroll, large generated artifact | Owner live | Owner live |
| Missing browser runtime, worker failure, crash/teardown | Controlled fallback | Controlled fallback |
| Stale bridge/review, injected strings, path/network attempts | Deterministic + adapter test | Deterministic + adapter test |

**Ship Monaco only if** both owner platforms pass correctness/security/offline
cases, the native prerequisites are supportable, and measured editor benefit
outweighs resource/packaging cost on the 6 GB machine. Otherwise use bounded
AvaloniaEdit and record the failed gate. Native Wayland-only support remains
Unknown until actually tested; do not force a system dependency installation
or alter the owner's display configuration during planning.

**Non-goals:** debugger, terminal, package manager, Git client, compiler/LSP
platform, VS Code extension host, executable artifact preview, broad WebView
abstraction serving unrelated views.

## 4.5 R33 continuation implementation reconciliation

On 2026-09-12 the bounded editor path was implemented in
`src/Hermaeus.Desktop/Views/WorkspaceEditorView.axaml.cs`:

- `Avalonia.Controls.WebView` 12.1.0 is the only new Desktop dependency and
  Monaco 0.56.0's minified `vs` bundle is copied into the application output.
- The view tries a local `file`-based Monaco page first and keeps a native
  AvaloniaEdit editor available as the fallback when the bundle, adapter,
  navigation, bridge, theme update, or character limit fails.
- The host sends serialized document data and receives only `ready`, `changed`,
  and `error` messages. The exposed editor operations are `setDocument`,
  `getText`, `setTheme`, `focus`, `layout`, and `dispose`; the page has no
  filesystem, process, navigation, or application command surface.
- The bundle and bridge contract are covered by `R33UxAndPetTests`, and the
  solution build verifies that the local assets are present in Desktop output.
- The continuation bounds the editor host inside the Agent stack, reattaches
  AvaloniaEdit after a Monaco failure or visual-tree detach, reapplies the
  current document text, and keeps the fallback editor stretched inside the
  bounded host. This repairs the same unbounded-measure failure class recorded
  in `MarkdownViewer.cs` without treating static layout proof as native GUI
  proof.

This earns a local implementation boundary, not the full Monaco ship gate.
The owner must still test WebView2 on Windows and the selected WebKitGTK/WPE
path on Pop!_OS, including worker startup, offline resource requests, keyboard
and IME input, copy, accessibility, focus, resize, DPI, large artifacts,
teardown, and fallback. No claim is made that a source build or static bundle
inspection proves those observations. If the native adapter or worker contract
fails, the visible editor must remain the functional AvaloniaEdit path.
