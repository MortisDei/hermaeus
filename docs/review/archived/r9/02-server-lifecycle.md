# 02 - Server lifecycle

## Problem statement

Managed llama-server children are only cleaned up on graceful exit
(`MainWindowViewModel` calls `Services.StopAll()`; `desktop.Exit`
disposes the service provider). When the app crashes, children are
orphaned: after the 2026-07-15 crash an orphan held port 42069, 3 GB
RAM, and 33 GPU layers. The next session's Start attempts then fail
instantly (port in use), and `ServerProcessManager` surfaces that
badly: the button flips from "starting" back with the actual cause
buried in the log ring. The owner clicked Start four times before it
took. Three fixes: children die with the app unconditionally, port
conflicts are diagnosed before launch, and Starting-state transitions
always say why they failed.

## 2.1 Job object: children die with the app

On Windows, create one job object at first server launch with
`JOBOBJECT_EXTENDED_LIMIT_INFORMATION.LimitFlags =
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, and assign every process
`ServerProcessManager` starts (src/Aether.Services/ProcessManagement/
ServerProcessManager.cs, `StartAsync` line 42, and the auto-tune probe
processes around line 220) to it via `AssignProcessToJobObject`. The
job handle lives for the process lifetime; when the app dies, however
it dies, the OS kills the children. Voice engine processes (XTTS /
Kokoro process managers) join the same job if they share the process-
launch path; if they have their own manager, apply the same treatment
there. Non-Windows: no-op behind an `OperatingSystem.IsWindows()`
guard (the app is Windows-first; do not add a Linux equivalent
speculatively).

P/Invoke lives next to the existing Win32 usage patterns (see
`GlobalHotkeyService` for style). Failure to create or assign the job
logs a Warning and never blocks the launch.

**Acceptance criteria**

- Manual verification (documented in the PR/commit notes): launch a
  server, kill the app process from Task Manager, confirm the
  llama-server exits within a second or two.
- Job creation failure path unit-tested to the extent practical
  (wrapper interface with a fake); the P/Invoke itself is exempt from
  coverage.
- No behavior change on graceful stop (StopAll still runs first).

## 2.2 Port preflight with a named owner

Before launching, `StartAsync` checks whether the configured port is
already listening (`IPGlobalProperties.GetActiveTcpListeners()` is
sufficient; loopback only). If it is, do not launch: set an
`ErrorMessage` that names the port and, where obtainable, the owning
process (PID and process name via `GetExtendedTcpTable` P/Invoke;
if the lookup fails, the port alone still gets named). Suggested
message shape: "Port 42069 is already in use by llama-server (PID
12852). Stop that process or change this server's port." Never
auto-kill.

**Acceptance criteria**

- Test: bind a listener on a free port in-process, call `StartAsync`
  with that port, assert Error status without a process launch (fake
  or spy on the process-build step) and a message containing the
  port number.
- Owning-process naming is best-effort; the code path with a failed
  PID lookup still produces the port-level message.

## 2.3 Orphan detection with explicit user-approved stop

At startup (and on Services view refresh), for each configured
managed server whose status is not Running, if its port is occupied
by a process whose executable path equals the server's configured
`ExecutablePath` (exact path match after normalization; anything else
is treated as an unrelated process and only reported, never
stoppable), show a banner in the Services view: "A llama-server from
a previous session is still running on port 42069 (PID 12852)." with
a Stop button. Stopping is an explicit user click, kills only that
PID after re-verifying its executable path, and logs the action.
Unrelated processes on the port get the 2.2 message with no Stop
button. Security posture: the app must never terminate a process it
cannot positively identify as its own configured server binary.

**Acceptance criteria**

- Path-mismatch case shows information only, no Stop affordance.
- Stop re-verifies executable path immediately before kill (PID reuse
  guard) and refuses on mismatch.
- Logic factored so the identify/verify decision is unit-testable
  with fakes (process enumeration behind an interface).

## 2.4 Honest Starting-state transitions

`ServerProcessManager` can leave "why did it stop starting" unclear:
`StartAsync`'s `OperationCanceledException` path (line 73-77) reverts
Starting to Stopped with no message, and `OnProcessExited` (line
491-497) only acts when status is Running. Guarantee: every
transition out of Starting that is not Running carries either an
`ErrorMessage` or (for user-initiated cancel/stop) a log line naming
the cause, and the exit code plus the last few log-ring lines are
folded into `BuildErrorMessage` when the process died before /health.
Additionally `StatusChanged` and `LogLine` fire on worker threads
while `ServicesViewModel` handlers (src/Aether.ViewModels/
ServicesViewModel.cs:125-144) set bound properties directly; that
marshaling is covered by doc 03 and must land with it.

**Acceptance criteria**

- Test: process that exits immediately (fake process seam or a real
  short-lived executable) produces Error status with exit code and
  log tail present in `ErrorMessage`.
- Cancel during health wait yields Stopped plus a "cancelled" log
  line; no silent revert path remains (enumerate the transitions in
  a test over a seam if practical, otherwise assert on the two known
  paths).
