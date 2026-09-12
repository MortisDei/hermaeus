# 09. Security, platform and release boundaries

## 9.1 Installed Linux launch investigation

**OO:** current application/menu launcher fails; an older installation worked.
**RF/local observation, 2026-09-07:** installed desktop entry contains an
unquoted Exec path with a space in the package-directory name. The intended
`Hermaeus -> app/hermaeus-app` link resolves to an executable file. The first
space-delimited token names a nonexistent executable. The installed runtime
configuration exists and this host has .NET 10 runtime 10.0.11. These checks do
not prove all native dependencies or display startup succeed.

Read-only GLib `Gio.DesktopAppInfo.new_from_filename` rejected the installed
entry with `constructor returned NULL`. A temporary copy under `/tmp` with the
same Exec value double-quoted was accepted. Neither entry was launched. This
isolates a concrete entry-parsing defect; it does not claim a visible window
pass or that no second startup issue exists.

**RF source:** `build.sh:144-201` derives installation directory from package
basename and writes `Exec=$INSTALL_DIR/Hermaeus` at line 183. A renamed extracted
package or a space in XDG_DATA_HOME can trigger the same class. This is not a
reason to blame COSMIC, require a shell wrapper or change .NET versions.

The [Desktop Entry Exec specification](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html)
requires quoting reserved characters and separate handling of literal percent
field codes. Shell escaping is not desktop-entry escaping.

**P outcome:** installation from supported paths creates a valid desktop entry
whose executable, argv and working-directory assumptions match the actual app.
**Boundary:** package generator and parser-level regression coverage. Encode Exec
according to desktop-entry rules; either correctly encode or explicitly reject
unrepresentable path forms. Audit icon/other path-valued keys using their own
rules. Determine whether a Path key is needed from executable-relative behavior,
not habit. Preserve native apphost links and installed window identity.

**Verification:** scratch package/install using XDG roots under temporary paths;
spaces, quotes, backslashes, percent, dollar/backtick and non-ASCII path cases;
parse and harmless argv/cwd fixture; move/extract/reinstall; actual owner menu
launch with missing-runtime/error visibility. Audit uninstall's matching package
resolution without deleting real installed data. Windows launcher stays a sibling
platform check, not an assumed same root cause.
**Dependencies:** none for generator repair; shared lifecycle for visible startup
and shutdown evidence. **Non-goals:** repairing owner installation during planning,
new installer ecosystem, auto-update, self-contained default merely as a guess.

## 9.2 Doctor remediation must name the target

**RF:** `DoctorService.BuildCheck` infers kind from FixLabel; DoctorViewModel
maps Category to a page and MainWindow assigns ActivePanel. The flow does not
resolve a stable server/setting/control target. A displayed actionable finding
can therefore send the owner to a broad unrelated-looking surface.

**P outcome:** a typed remediation target resolves panel, section and stable
entity id, reveals the relevant control, and gives a reason when unavailable.
Keep diagnosis in Services and presentation/navigation in Desktop. Fixable versus
navigable versus external-link actions are explicit, not inferred from wording.
After navigation, revalidate that the entity still exists. An external release
link remains an external link, not an install/update approval.

**Boundary:** small Core navigation/action DTO plus Desktop mapping; extend the
existing check/action model rather than adding a Doctor subsystem. Share target
resolution with recommendation/evidence navigation. Remediation never bypasses
settings, runtime trust or confirmation flows.
**Dependencies:** doc 02 application identity and doc 06 retained details.
**Verification:** real finding -> action -> correct section/entity/focus, missing
entity, unsupported remediation, async target load, return path; V10 plus owner
Linux/Windows. **Non-goals:** automatic repair of every finding, Doctor becoming
an owner of every subsystem.

## 9.3 Restore resource budget

**RF:** `BackupService.RestoreAsync:105-157` preflights target paths, checks
existing files and reparse ancestors, then extracts entries. Latest main history
includes containment/CodeQL repairs. Do not claim reparse containment is absent.
The storage skill's statement that some pre-existing symlink ancestors are not
rejected is stale against this source; record a documentation correction with the
owning hardening batch, not a duplicate implementation. No skill was edited here.

**Selected P:** preflight entry count, individual and total uncompressed size
with overflow-safe arithmetic, and enforce actual expanded bytes while copying.
Use configurable/internal reviewed budgets appropriate to legitimate backups;
freeze defaults only after inspecting representative nonprivate backup sizes.
Compression ratio alone is not a sufficient resource budget. Cancellation must
interrupt large-entry copying, not only run between entries. Reject duplicate
normalized destinations and platform path aliases as part of the same extraction
preflight so a validated inventory describes what is actually written.

Bound temporary storage/disk usage and report refusal before mutation where
possible. Define partial-extraction cleanup/receipt and overwrite semantics;
never delete existing owner files to clean a failed restore. Reuse current
containment primitive and test CodeQL-recognizable flows without weakening checks.
A full transactional backup migration engine is not required for this bounded
repair. Existing explicit overwrite confirmation remains necessary.

**Dependencies:** current BackupService boundary and isolated fixtures.
**Verification:** many entries, huge declared sizes, expansion exceeding budget,
integer overflow, duplicate/case aliases, cancelled large entry, traversal,
symlink ancestor, overwrite refusal and exact failure cleanup. No real backup
restore is performed during planning. **Non-goals:** encryption/vault migration,
cloud backup, sync, signed installer program.

## 9.4 CodeQL and CI: actual evidence versus gate

Read-only GitHub metadata on 2026-09-07:

- Main at `c944feb` has successful Ubuntu/Windows build-and-test checks and
  successful CodeQL Analyze checks for actions, C/C++, C#, JavaScript/TypeScript
  and Python. Default code-scanning setup is configured. There is no repository
  CodeQL workflow file because default setup is in use.
- The active main ruleset requires only `build-and-test (ubuntu-latest)` and
  `build-and-test (windows-latest)`, with strict status policy and PR requirement.
  The other ruleset protects version tags. Neither inspected ruleset requires
  CodeQL. Legacy branch-protection lookup returns 404; the ruleset, not that
  legacy endpoint, is the relevant enforced main boundary.
- [Existing PR #15](https://github.com/MortisDei/hermaeus/pull/15) is merged,
  same repository on both sides, head `d3dc909329fa164457d562a52154056c92c5f057`.
  [CI run 34007907871](https://github.com/MortisDei/hermaeus/actions/runs/34007907871)
  has event `pull_request`, branch `r32/round`, that head SHA and conclusion
  success. This supplies the previously missing PR attachment evidence for R32.
  It does not prove every de-duplication/cancellation race or R33's future PR.

**P:** keep CodeQL results blocking in engineering acceptance; the owner must
choose/enforce the corresponding repository merge rule if CodeQL is intended to
be a technical gate. Do not call successful analysis an enforced gate. No rule,
setting, PR or workflow was modified during planning. A later owner settings
change is separate from product implementation and must remain visible as an
external gate if not authorized.

For the actual owner-opened R33 PR: capture PR head and tested merge context,
required check names/ids, CodeQL analyses and enforcement state, branch-skip and
superseded-run behavior, Ubuntu and Windows outcomes, and relevant logs. Do not
manufacture a PR or modify CI just to exercise the gate. Keep trusted-push-only
Defender exclusions; PR/fork code never receives that exemption.

The CI workflow's TRX command currently lacks an external results directory even
though local task instructions require it. Record this bounded workflow hygiene
debt; change only in a later explicitly scoped workflow/documentation correction,
not as an exercise during planning. Do not conflate repository test artifacts
with private owner test output.

## 9.5 Bounded security review decisions

Keep deterministic Agent risk, scoped named API tokens, redaction, atomic state
writes, pinned downloads and safe workspace paths. New headless/editor clients
must enter those same boundaries. Editor protocol/network restrictions are in
doc 04; Agent review and crash replay are in doc 03. Application diagnostics stay
provider-neutral with supplementary JSONL, doc 05.

Current security-roadmap items remain separately tracked: network-affecting
runtime flag policy, RAG file-size enforcement, web-ingest allowlisting, new
provider redaction formats, fallback-vault visibility/AEAD, backup manifest and
preview, signing, tool scoping and binary trust display. Select only restore
budgets, applicable preapproval path consistency, lifecycle/editor/API boundaries
and demonstrated findings for R33. The roadmap's reference to the API's old
five-endpoint count is stale; count current routes and scope by capability/risk,
not a historical numeric trigger. Signing, AEAD and licensing changes are not
smuggled into this round.
