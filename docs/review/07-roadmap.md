# 07. Roadmap

Ships as **0.24.0-alpha** (feature round with a stability spine; minor bump per the
r5 precedent). Version bump in `Directory.Build.props` only.

## Sequencing

1. **Doc 01 first, in order.** 1.1 is the active crash; 1.4 is the double-init bug
   that poisons everything measured after it (timings, Doctor, autostart). Land and
   verify both before trusting any later manual testing session.
2. **Doc 02** (2.5 -> 2.1 -> 2.4 -> 2.2 -> 2.3). The UI deletion is trivial and
   unblocks the Services card layout; update-flow changes last since they need the
   most careful fakes.
3. **Doc 03** (3.2 -> 3.3 -> 3.1). The New-task button and honesty note are small;
   ContinueTaskAsync touches AgentService and needs the scenario-style test.
4. **Doc 06** (6.2 -> 6.5 -> 6.3 -> 6.4 -> 6.1 -> 6.6). Mostly XAML; 6.3 before 6.4
   so the thinking bubble tests run against the final scroll behaviour.
5. **Doc 04** (4.4 -> 4.3 -> 4.1 -> 4.2). The stop button is pure win; chunking
   changes need listening verification, do them when the rest is stable.
6. **Doc 05 last** (5.1 -> 5.4 -> 5.2 -> 5.3). Largest new surface. 5.3 (vision) is
   the only item in this round allowed to slip to r20 if the payload work reveals a
   llama-server compatibility rabbit hole; if it slips, say so explicitly in the
   final report, do not half-land it.

## Test budget

~40-55 new tests from the current 967. Every new harness-style method must be
registered in `XunitHarnessTests.HarnessCases` as it is written (the
HarnessRegistrationGuardTests reflection guard fails otherwise).

## Docs and security

- `docs/features.md`: attachments (docx/pdf/images with honest limits), artifacts,
  voice stop, agent continue/new-task, benchmark rankings, truncation notice.
- `docs/agent.md`: continue semantics and the premature-complete note.
- `docs/voice.md`: chunking/pause behaviour, dropdown pickers, stop.
- `docs/benchmarks.md`: what Score means (same wording as the UI caption).
- `CHANGELOG.md` with the 10-version FIFO rule.
- `docs/security-review.md` r19 subsection: DocxTextExtractor and PdfTextExtractor
  parse untrusted files (zip bomb cap, malformed-input containment); chat artifacts
  introduce a user-named write into a fixed sandbox folder (sanitization rules);
  crash logs relocate into the data root; vision payloads embed local image bytes
  into requests to the (localhost or explicitly-configured remote) chat endpoint.

## Explicit rejections (do not implement, do not re-propose)

- No chat-side model-initiated file writes or chat tool loop (agent territory).
- No OCR, no encrypted-PDF support, no PDF font CMap decoding this round.
- No auto-restart of servers at any time OTHER than the user-clicked update flow.
- No background update checks; update remains manual.
- No renaming of existing model files during the llm-folder casing fix.
- No new NuGet packages for docx/pdf/zip (BCL only; that is the point).
- No rewriting of benchmark scoring math; 6.6 is presentation.
- No automatic reopening of prematurely-completed agent tasks; the user clicks
  Continue.
- Markdig stays the markdown engine; 1.1 is a null-guard plus containment, not a
  renderer swap.

## Final report expectations

What changed, build/test result (zero warnings, full suite), which items were
verified live in the running app (1.1, 1.4, 6.3 at minimum), and any deliberate
deviations with reasons, per the working agreement.
