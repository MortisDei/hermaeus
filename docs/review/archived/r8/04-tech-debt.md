# 04 - Technical debt

## Problem statement

The debts below have either already caused bugs (unregistered dead
tests found in r6), block review (multi-class kilofiles), or are
leftovers from the unreviewed optimization commits. No TODO/FIXME
markers exist in the codebase; this list comes from audit, not
grep.

## 4.1 Test registration guard

The custom harness registers most tests as `HarnessCase` entries in
`src/Aether.Tests/XunitHarnessTests.cs`. r6 found two
`TraceBindingTests` methods that existed but were never registered:
they silently didn't run for multiple releases. Nothing prevents
that recurring.

Add one reflection-based guard test: enumerate public parameterless
(or ct-taking) methods on the known harness test classes whose names
match the test naming convention, and assert each appears exactly
once in the registered `HarnessCase` list (match by method name).
Maintain an explicit allowlist for intentional helpers so the guard
stays honest. The implementer defines the convention from what is
actually in the file (do not invent attributes; this repo
deliberately avoids converting the harness to plain xunit facts,
see doc 05 rejections).

**Acceptance criteria**

- Temporarily adding an unregistered `FooTests.BarBehavesWell`
  method makes the guard fail with a message naming it (demonstrate
  in the PR description, then remove the dummy).
- Guard runs as part of the normal `dotnet test` invocation.

## 4.2 Split SettingsSectionViewModels.cs

src/Aether.ViewModels/SettingsSectionViewModels.cs is 881 lines of
multiple independent section ViewModels in one file. Split
mechanically into one file per class, no code changes beyond
`using` moves. Same for any other multi-class file over ~500 lines
found while doing it (report, do not expand scope silently).

**Acceptance criteria**

- Pure move: `git diff --stat` shows deletes/adds only, and a
  before/after build both produce zero warnings; no namespace or
  accessibility changes.

## 4.3 DoctorService internal split

src/Aether.Services/DoctorService.cs is 1411 lines, mostly a flat
list of check implementations inside `ScanAsync`'s orbit. Split into
`partial class DoctorService` files grouped by domain
(DoctorService.Storage.cs, DoctorService.Models.cs,
DoctorService.Voice.cs, etc.), public API unchanged. Do not
resurrect the deleted InspectionEngine abstraction (r6 removed it as
dead; doc 05 rejection).

**Acceptance criteria**

- No public or internal API change (same type, same members);
  existing Doctor tests pass untouched.
- Each partial under ~400 lines.

## 4.4 Phonemizer table cleanup

Superseded by doc 01: the inline dictionary (with its duplicate
`new`/`may` keys) is deleted when CMUdict lands. If doc 01 slips out
of the round, fix the duplicates and add a golden test for the
inline table as a standalone item instead.

## 4.5 Corrections from the unreviewed commits (tracking item)

Covered elsewhere, listed here so the round explicitly closes the
review debt on `ad618da` + `aea2326`:

- Memory recall threshold + N+1: doc 03 item 3.3.
- Warm-up on critical path: doc 03 item 3.2.
- Dead thinking-indicator markup: doc 02 item 2.5.
- Kept as-is (reviewed, fine): LlamaCppService structured logging
  and 50-entry context cache cap, BenchmarkViewModel restart guard,
  ChatView trace FallbackValues, ServicesViewModel model-path guard
  (`File.Exists` on the UI thread is acceptable for a local path
  check on an explicit user action).

**Acceptance criteria**

- CHANGELOG for this round names both commits and states they are
  now reviewed, with the two behavioral corrections called out.
