# Review round 19: daily-driver truth

The owner now uses Aether as their primary AI workstation (Unsloth and Hermes are being
uninstalled). This round is anchored by a live field report from that daily use: one hard
crash with the root cause found in the crash log, several places where the app silently
lies or hides state (Doctor's clean-shutdown check, benchmark rankings, memory pills),
several places where a working backend has no UI affordance (stop voice, continue agent
task, new agent task), and two genuine feature gaps (document/image attachments in chat,
chat having no way to hand files back to the user).

Every diagnosis below was code-verified against the working tree at v0.23.0-alpha
(commit 24a6cde) and, where possible, against the owner's live data root (`C:\AI\Aether`)
and the crash logs in the build output directory. File:line references are exact at spec
time; re-verify before editing, the tree may have moved.

## Documents

| Doc | Theme |
| --- | --- |
| `01-stability-and-truthful-failure.md` | The MarkdownViewer crash, silent max-token truncation, crash-log surfacing, the tray-restore double-init bug and the Doctor clean-shutdown check it poisons |
| `02-services-models-and-update.md` | Model-card defaults feeding Services, update-while-running, CUDA runtime re-downloads, llm folder casing, manual path box removal |
| `03-agent-continuity.md` | Continuing a stopped/finished task, starting a new task without restarting the app, premature-complete honesty |
| `04-voice.md` | Word clipping at chunk seams, punctuation adherence, voice dropdowns, a stop-playback affordance |
| `05-chat-attachments-and-artifacts.md` | .docx/.pdf/image attachments; a per-conversation artifacts folder so chat output can become files |
| `06-ui-truth-and-polish.md` | Memory pill observability, System Overview ordering, chat scroll/borders/thinking feedback, benchmark rankings redesign |
| `07-roadmap.md` | Ships as 0.24.0-alpha; sequencing, test budget, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. Several past rounds found spec premises wrong (r16, r18);
  each item below states what was verified and what still needs a live check.
- No em dashes anywhere, zero-warning build, tests must pass, HarnessCases registration
  for every new harness-style test method.
- The agent approval-gate posture is non-negotiable; nothing in this round adds an
  unattended write path outside the sandboxed folders each item names.
- Update `docs/features.md`, the relevant workflow docs, and `CHANGELOG.md` for
  user-visible changes. Do not document planned behaviour as existing.
