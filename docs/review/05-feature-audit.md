# Feature Audit (2026-07)

Ratings: **Essential** / **Refine** (good, needs work) / **Over-engineered** /
**Merge** / **Deprecate** (eventually). Judged against long-term value per
maintenance dollar.

## Chat

| Feature | Verdict | Notes |
| --- | --- | --- |
| Chat + history, FTS search, folders/tags/pins/archive | **Essential** | Core surface. Folders *and* tags *and* pins *and* archive is organizational feature creep at the margin — watch it, don't grow it. |
| File attachments with bounded context blocks | **Essential** | The read-once-at-send + summary-in-history design is genuinely good. |
| Context usage indicator | **Essential** | Differentiator; keep. |
| Context Inspector | **Essential** | This is the product thesis in one panel. Promote it, don't hide it. |
| Chat Trace Viewer | **Refine** | Right idea; should share one trace model with RAG and Agent traces instead of "extending" it (three trace shapes today). |
| Compare Models | **Refine** | Useful, but overlaps benchmarking conceptually. Fine as-is; resist growing it into a second benchmark system. |
| Quick Chat + tray + hotkeys | **Essential** | This is what "native" buys; core to identity. |

## Memory

| Feature | Verdict | Notes |
| --- | --- | --- |
| Persistent memories + panel | **Essential** | — |
| Auto-summary extraction pipeline | **Refine** | Valuable but trust-sensitive; needs visible provenance ("saved because of conversation X") to match the audit posture. |
| Session Usage panel | **Merge — DONE** | Folded into the Memories panel: a conversation filter drives per-conversation counts and a scoped CSV export; the standalone panel/ViewModels are deleted. |
| Memory encryption toggle | **Over-engineered** | A toggle means users must understand a threat model. Pick a default (encrypt) and delete the option. |
| Fixed categories (facts/preferences/…) | **Refine** | Categories should give way to scopes when memory unifies (see Opportunities #4). |

## Agent Workbench

| Feature | Verdict | Notes |
| --- | --- | --- |
| Read-first task runner, risk gates, traces | **Essential** | The most strategically important subsystem. The discipline here is the moat. |
| Draft patch queue + baseHash staleness | **Essential** | Elegant: correct, minimal, auditable. |
| Workspace file browser in-panel | **Merge** | A file manager inside a chat app is scope creep; keep only what patch review needs. Users have file managers. |
| Workspace memory notes | **Merge** | Into unified memory (workspace scope). |
| Workspace profile analysis / Explain Workspace | **Essential** | This is "project intelligence" and feeds everything else. |
| Capability disclosure callout | **Essential** | Cheap, honest, on-brand. |

## Model Management

| Feature | Verdict | Notes |
| --- | --- | --- |
| Model + runtime profiles | **Essential** | Needs the capability model (Opportunities #1) underneath. |
| Managed llama-server + GPU auto-tune | **Essential** | Hard to build, real differentiation vs. every WebView competitor. |
| Benchmarking suite | **Over-engineered, trimmed** | Removed the duplicate zip bulk-export path and the All/Latest/Last-N ranking-mode picker; rankings now always show latest-per-model, the one view that answers "which model should I use." Triple CSV/JSON/MD export per run, the run-info/case-info dialog split, and the 12 starter suites are deliberately untouched (see docs/review/06-technical-debt.md item 1 and CHANGELOG) since cutting those changes user-facing behavior and test expectations, not just duplication. |
| Doctor | **Essential / Refine** | Essential function, wrong architecture (god service — see review §1). |

## RAG

| Feature | Verdict | Notes |
| --- | --- | --- |
| Ingest (structure-aware, batched, resumable, cancellable) | **Essential** | Mature and well-designed. |
| Hybrid retrieval + citations + refusal | **Essential** | Refusal-on-weak-context is rare and valuable. |
| ONNX reranker (explicit install) | **Refine** | Good; keep it strictly optional and contained. |
| Query planner multi-variants + traces | **Refine** | Verify variants earn their latency with the eval harness you built for exactly this. |
| Eval harness | **Refine** | Excellent for development; as a *user-facing panel* it's niche. Consider demoting to a power-user/dev surface rather than growing its UI. |
| Dataset Manager | **Essential** | Health surfacing (stale/missing/reindex warnings) is exactly right. |
| Web loader (explicit URLs, off by default) | **Essential** as-is | The restraint is the feature. Do not grow into a crawler. |

## Voice

| Feature | Verdict | Notes |
| --- | --- | --- |
| Voice readback | **Refine** | Legitimate feature. |
| Four providers (Kokoro, F5-TTS, XTTS v2, OpenAI) with Python venvs, per-provider process managers, generated scripts, Python health validation | **Over-engineered** | The highest maintenance-cost-per-user area in the app: a shadow Python distribution with per-provider version constraints (3.11 vs 3.12!). Converge on one great ONNX-based local provider (Kokoro-ONNX) + OpenAI-compatible remote; demote XTTS/F5 to unsupported/advanced or deprecate. Voice cloning workflows are a different product. |

## Setup & System

| Feature | Verdict | Notes |
| --- | --- | --- |
| Setup Wizard | **Essential** | The bridge from enthusiast to professional user. |
| Local AI setup scans + gated downloads + SHA256 pinning | **Essential / Refine** | Right behavior; belongs in the unified check/fix registry. |
| Trust & Safety scan | **Merge** | Into the check registry with Doctor/Privacy Audit — one inspection engine, filtered views. |
| Privacy Audit dashboard | **Essential** | Signature feature; make it the marquee screen. |
| Runtime logs + redaction | **Essential** | Redaction-before-persist is correct and rare. |
| Backup/restore/data-root migration | **Essential** | Boring excellence. |
| Local tasks/reminders/automations | **Deprecate — DONE** | Removed entirely: `TasksViewModel`, `AutomationScheduler`, `TasksView`, the settings-schema `Tasks`/`Automations` lists, and the sidebar entry point. It composed with nothing and competed with every OS's native reminders. If agent-scheduling infrastructure is needed later, it should be built as that (e.g. under the Agent Workbench), not resurrected from this code. |
| Toast system, System Overview | **Essential** | — |

## Pattern to note

Nearly everything rated **Merge** or **Over-engineered** shares a root cause:
features were built as parallel verticals (own store, own panel, own checks)
rather than composing. The fix is the same short list every time — unified
memory, unified check registry, unified trace model, capability-based
providers. Fix those four and this audit's middle column mostly resolves
itself.
