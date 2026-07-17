# r12-04: Roadmap

## Version

Implementing this pack is `v0.17.0-alpha` (minor bump from
0.16.0-alpha, consistent with r10/r11).

## Sequencing

Work in this order; later items assume earlier ones landed.

1. **02 item 2.6 (RunOnUi inline-on-UI-thread)** first: it changes the
   semantics the 2.1 fix builds on and is the smallest diff.
2. **02 items 2.1, 2.2** (toast history ordering, SendAsync catch):
   isolated, user-visible, low risk.
3. **01 items 1.1, 1.2** (wizard migration, save rollback): the data
   integrity core of the round. 1.1 depends on nothing; 1.2 decides
   the copy-vs-reload strategy that 1.5 reuses.
4. **03 items 3.1, 3.2** (first-run init, init isolation): touches
   MainWindowViewModel startup; do after 1.x so the wizard flow being
   tested is the fixed one.
5. **01 items 1.3, 1.4, 1.5** (live-settings writes, rebuild storm):
   1.4 is the largest diff of the round (Rebuild becomes a differ) and
   benefits from the tests added in 3.x.
6. **03 items 3.3, 3.4, 3.5** (model re-match, agent workspace).
7. **02 items 2.3, 2.4, 2.5** (debounce, log batching, load latch).
8. **03 items 3.6, 3.7, 3.8** (RAG target trap, reindex label, rerun
   guards), then **3.9 batch** and **1.6, 1.7**.

## Test expectations

- Every numbered item names its acceptance tests; the pack adds roughly
  15-20 new unit tests, all in Aether.Tests, run via
  `dotnet test src/Aether.Tests/Aether.Tests.csproj`.
- Threading tests use the existing armed `UiThreadGuard` pattern from
  r9; no new test infrastructure should be required beyond a counting
  `SynchronizationContext` fake (2.4, 2.6).
- Zero-warning build (`TreatWarningsAsErrors`) stays mandatory.

## Security review touch

Update `docs/security-review.md` for:
- 3.5: the agent no longer treats the user profile as an implicit
  workspace (shrinks the default read surface to nothing until the
  user picks a root).
- 1.5: trust scans become read-only with respect to settings.
No new network, process, or secret surface is introduced by this pack.

## Explicit rejections

Considered and rejected this round; do not re-propose without new
evidence:

- **Making ViewModels transient instead of singleton.** The singleton
  + explicit Reload/Load lifecycle is now load-bearing (event wiring,
  process managers); switching lifetimes would be a bigger regression
  risk than fixing the reload discipline itself.
- **A general settings-freeze/immutable-snapshot architecture.** Item
  1.2's copy-on-save (or reload-on-failure) is sufficient; a full
  immutable settings model is r1-scale surgery for marginal benefit.
- **Virtualized log view or moving Logs off ObservableCollection.**
  Batching (2.4) is enough at the current log cap; revisit only if
  profiling still shows dispatcher pressure.
- **Auto-migrating the data root when the wizard is skipped.** 1.1
  only wires the existing migration into the wizard's save; inventing
  new migration behavior stays out of scope.
- **Debounce framework/library.** Reuse the 300 ms CTS pattern already
  in MainWindowViewModel; three call sites do not justify an
  abstraction beyond, at most, one small shared helper.
