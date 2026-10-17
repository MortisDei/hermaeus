# R34 process evidence sources

These are the exact small console-driver sources used for process-interruption
evidence. They are retained as review artefacts, outside the production build and
test-project discovery. They add no package, framework, runtime manager, security
boundary or persisted product format. Results and limits belong to
[15](../15-native-and-vault-evidence.md), not to the existence of these sources.

## Vault driver

Copy [VaultProcessProbe.cs.txt](VaultProcessProbe.cs.txt) to `Program.cs` in a
fresh temporary .NET 10 console project with nullable and implicit usings enabled.
Name its assembly `VaultProcessProbe`. Reference the current built
`Hermaeus.Services.dll` and `Hermaeus.Core.dll` from the test output with ordinary
binary `Reference`/`HintPath` entries. Do not use `Hermaeus.Tests` as its assembly
name or change friend access. No NuGet package reference is needed.

Build the external project, then invoke through the dotnet host:

```text
dotnet <output>/VaultProcessProbe.dll parent <dedicated-scratch-parent> <preserved-R33-package-app-directory>
```

The driver creates its own unique child roots beneath that scratch parent. It
uses the preserved R33 assemblies to create/read synthetic legacy credentials,
and reflection to reach the actual current internal transaction and decoder.
The original keychain-disable variable is scoped to its process and children.
Never point it at owner settings or a real credential vault.

Its parent owns the child handles it terminates, verifies barrier signals and
lock contention, and bounds waits. Crash-created temporary files are retained
because a terminated process cannot execute finally cleanup. Linux scratch data
must be on the native Linux filesystem, rather than the mounted Windows drive,
to establish Linux locking behaviour. Use platform-correct binary reference and
control-package paths. This is process-death proof at selected boundaries;
power loss, hostile non-cooperating writers and desktop acceptance stay separate.

## Agent approval recovery driver

Copy [AgentRecoveryProbe.cs.txt](AgentRecoveryProbe.cs.txt) to `Program.cs` in
another external .NET 10 console project, with nullable and implicit usings.
Reference the existing Core, Agent and Services assemblies using binary
references with `Private=false`. After building, write `binary-directory.txt`
beside the probe DLL containing the absolute current test-output directory.
The driver resolves those exact managed assemblies and the platform-specific
SQLite library beneath its `runtimes` directory. It never calls a model; unused
planner/context substitutions throw if invoked.

Use the retained owned-worker parent on the matching platform:

- [Run-AgentRecoveryProbe.ps1.txt](Run-AgentRecoveryProbe.ps1.txt): copy outside
  the checkout with a `.ps1` extension, then run with `-ProbeDll <absolute DLL>`.
- [Run-AgentRecoveryProbe.py.txt](Run-AgentRecoveryProbe.py.txt): copy outside
  the checkout with a `.py` extension, then run `python3 <parent.py> <absolute DLL>`.

The parents generate dedicated native TEMP case roots, retain stdin while
checking the READY PID, and terminate only their returned worker handle. They
exercise pre-write, post-write and post-write-plus-synthetic-external-edit
recovery, including two fresh initialisations and stale-approval refusal.
Outputs contain receipt identities and hashes of synthetic state, not owner
paths or file contents. Existing case roots are never reused or deleted.

## Secret component/native backend driver

Copy [SecretComponentProbe.cs.txt](SecretComponentProbe.cs.txt) to `Program.cs`
in a third external .NET 10 console project. Reference the current Core and
Services binaries and copy their existing managed dependency DLLs into that
external output. Nullable and implicit usings are enabled; no package is added.

```text
dotnet <output>/<console>.dll fallback
dotnet <output>/<console>.dll windows-os
```

`fallback` forces OS credentials off only inside the driver and checks actual
v3 persistence, reopen and provider reference resolution through a synthetic
HTTP handler. No network request is made. `windows-os` runs only on Windows,
positively preflights its random GUID target as absent, uses the existing OS
backend, checks fallback sibling/key preservation, then deletes only its exact
new synthetic target and confirms absence. It never enumerates credentials.
If cleanup fails, its output identifies only that exact synthetic target for
investigation. Do not substitute real credentials or owner settings.
