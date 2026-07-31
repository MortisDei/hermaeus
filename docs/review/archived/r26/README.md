# Review round 26: The workbench, made legible

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

r25 made the app tell the truth about what it did. r26 turns that on the
panel where it matters most and has never had it: the Agent workbench.

The Agent is the most capable surface in Hermaeus and the least usable one.
It is a single 1194 line view with thirteen top-level expanders, eight of
them open by default, thirty-eight buttons, four combo boxes and nine text
boxes, all stacked into one flat scroll under a header that cannot scroll at
all. Nothing in it is wrong on its own. Every panel was added by a round that
had a good reason. Nobody has ever gone back and asked what the panel is for.

And inside that panel sits a control that is not what its name says:

**The Review Queue is not a queue.** `ListReviewQueueAsync`
(`FileAgentTaskStateStore.cs:114`) selects tasks
`WHERE t.status IN ('WaitingForUser', 'Blocked') OR t.approval_count > 0`.
That second clause means every task that has *ever* been approved stays
listed forever. The row renders Approve and Reject buttons unconditionally,
and `AppendApprovalAsync` (`AgentService.cs:1054-1062`) accepts an approval
on a task with no pending action: it appends an approval record and sets the
task's status to `Running`. So clicking Approve on a finished task
un-completes it, and `ResumeAgentLoopIfRunnableAsync`
(`AgentViewModel.cs:1085`) then restarts the agent loop on a run that was
already done. Clicking Reject sets a terminal task to `WaitingForUser`.

The owner's report was "I can endlessly keep clicking approve." That is the
visible half. The other half is that each of those clicks resurrects a
completed task and spends tokens on it.

So: doc 01 makes the queue a queue and stops the approval path mutating a
task that has nothing pending. Doc 02 gives the workbench a shape. Doc 03
answers the owner's two standing daily-use questions ("what can it even do",
"did that actually work") from data the app already has. Docs 04 and 05 clear
four items off `deferred.md`.

## Documents

| Doc | Theme |
| --- | --- |
| `01-the-review-queue-is-a-queue.md` | The queue lists what needs a decision and nothing else; an approval with nothing pending is refused, not recorded; the queue refreshes itself when a run pauses |
| `02-a-workbench-you-can-read.md` | Four tabs inside the Agent panel, a pinned strip for the decision the agent is waiting on, a header that cannot eat the window, and the duplicated and implementation-verb controls removed |
| `03-what-it-can-do-and-whether-it-worked.md` | Capability text derived from the real tool registry and this workspace's own recipes, and a run outcome summary built from the ledger that already exists |
| `04-best-across-every-suite.md` | The cross-suite Best Overall column the owner asked for after r25, keyed by suite, with per-suite scores on click |
| `05-small-open-items.md` | The local API capabilities probe (deferred since r1), the clock-dependent test fix (deferred from r25 5.4), and docs |
| `06-roadmap.md` | Ships as 0.33.0-alpha; sequencing, test budget, descope order, housekeeping, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. Every file:line reference in this pack was
  exact against tree `35045fc` (v0.32.0-alpha, the r25 merge). Re-verify
  before editing. `AgentViewModel.cs`, `AgentService.cs` and
  `BenchmarkService.cs` are named hot spots in CLAUDE.md and move often.
- No em dashes anywhere. Zero warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- **No new NuGet packages.** Nothing in this round needs one. Doc 02 is a
  layout change built from Avalonia controls that are already in use
  elsewhere in this repository; if a UI package feels necessary, the answer
  is that `BenchmarkView.axaml:189` already does this with a `TabControl`.
- **The risk classification and the approval gate are not touched.** Doc 01
  makes the gate harder to bypass by accident, never easier. Any change that
  would let an action execute without an explicit, matching, fingerprinted
  approval is out of scope and out of bounds. Re-read
  `AgentService.cs:932-970` before editing that method.
- **Doc 02 is a move, not a rewrite.** Every panel that exists today still
  exists after doc 02, reachable in at most one click, bound to the same
  view model members. A restructure that quietly drops a panel is a
  regression with a redesign's alibi. Doc 02 lists the full inventory and
  where each item lands; check it off.
- **The decision the agent is waiting on is never behind a tab.** This is
  the rule that makes tabs safe here. A run that pauses for approval while
  the user is on the Workspace tab must still be visible and actionable.
- Schema changes are additive and go through `SqliteMigrationRunner`. Doc 01
  changes a `SELECT`, not a table. `agent/task_index.db` is rebuildable, so
  no migration is needed for the query change itself; an install that never
  opens the Agent panel must behave exactly as 0.32.0 does today.
- Update `README.md`, `docs/features.md`, `docs/agent.md`,
  `docs/benchmarks.md` and `CHANGELOG.md`. r25's doc-drift guard exists now;
  run it. Do not document planned behaviour as existing behaviour.
- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
  Icon-only controls need tooltips; the guard test scans axaml and fails
  without one. Doc 02 moves a lot of axaml past that guard.
- `docs/review/deferred.md` is updated at close-out, not at the end of the
  round in spirit only. Four items move to Closed this round; one gains a
  restated reason. See 06's housekeeping.
- This round lands via pull request per `docs/pull-requests.md`: branch
  `r26/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.
