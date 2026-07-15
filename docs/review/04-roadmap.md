# 04 - Roadmap

## Version

Implementing this pack ships **0.14.0-alpha**. The crash fix and
guard already shipped as **0.13.1-alpha** alongside this pack's
authoring commit; r9 implementation builds on it.

## Sequencing

1. **Doc 03 (UI thread safety)** first. It is mechanical, prevents
   the only known crash class, and the guard turns any writer missed
   during docs 01/02 work into an immediate, attributable failure
   instead of a field crash. Everything after benefits.
2. **Doc 01 item 1.1 (instrumentation)** next, before any latency
   fix, to capture a baseline on the owner's machine.
3. **Doc 01 items 1.2-1.5**, convicted by inspection and (after 1.1)
   by numbers.
4. **Doc 02 (server lifecycle)** last; it is independent and its
   manual verification step (kill the app, watch the child die) is
   easiest once everything else is stable.

## Test expectations

Rough guide, not a quota: 1.1 formatter + trace round-trip (3-4),
1.2 backfill relocation + cooldown (4-5), 1.3 fast-fail (2), 1.4/1.5
advisories (3-4), 2.2 preflight (2-3), 2.3 identify/verify seam
(3-4), 2.4 transitions (2-3), 3.3 architecture test (1, plus the
sweep keeps 517 green). Expect roughly 20-30 new tests. All tests
run without a live llama-server or network, per the standing rule;
process and HTTP boundaries get seams/fakes.

## Security review touch

docs/security-review.md gains an r9 subsection covering: job-object
process containment (2.1), the port-owner lookup (2.2, reads process
metadata only), and the orphan Stop affordance (2.3, the only place
the app terminates a process it did not start this session; document
the executable-path verification and PID-reuse guard as the
mitigations).

## Explicit rejections

Checked against archived rounds and rejected for r9; do not
re-propose without new evidence:

- **Auto-killing unrecognized processes on a conflicting port.** Only
  a verified own-binary orphan gets a user-clicked Stop (2.3).
- **Thread-safe collection wrappers or locks instead of marshaling.**
  Avalonia requires UI-thread mutation; synchronization does not
  satisfy that, it only hides the corruption window.
- **Arming UiThreadGuard from SynchronizationContext presence or
  type-name sniffing.** Proven wrong under xunit; explicit Arm() from
  the Desktop app is the contract.
- **A global exception handler that swallows dispatcher exceptions.**
  Masks corruption; the guard exists to fail loudly and early.
- **Making the 1.3 query-embed timeout user-configurable.** One more
  setting nobody can reason about; a constant is fine until evidence
  says otherwise.
- **Speculative context-size clamping or auto-tuning (1.5).** Advisory
  only; the owner chose that context deliberately.
- **A Linux job-object equivalent (2.1).** Windows-first app; no
  field evidence of the problem elsewhere.
