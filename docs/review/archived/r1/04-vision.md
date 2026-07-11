# Aether 2.0 — Vision

## The one-sentence product

Aether is the private AI layer of a personal computer: the place where a
person's models, their knowledge, and their machine's capabilities live under
one auditable roof — owned by them, running on their hardware, answerable to
no vendor.

## The problem it solves

Every serious AI tool today asks the user to surrender something: their data
(ChatGPT, Claude Desktop), their choice of model (all first-party apps), their
understanding of what is happening (agents that act first and explain never),
or their patience (LM Studio and Open WebUI expose raw plumbing and call it
control). Meanwhile the actual user need has converged: *"I want capable AI
that knows my stuff, works on my projects, and I can verify what it did and
where my data went."*

Aether's answer is distinctive because it treats **trust as a feature you can
inspect, not a promise you must accept**. The Context Inspector, traces,
Privacy Audit, risk-classified approval queue, and read-first agent are not
compliance checkboxes — they are the product. Aether 2.0 doubles down: every
byte that reaches a model is explainable, every action an agent takes is
attributable, every data flow off the machine is enumerable. No other product
in this space can say that, because their business models forbid it.

## Who it is for

Primary: **the technical professional with things to protect** — developers,
consultants, lawyers, researchers, clinicians-adjacent knowledge workers who
have client code, contracts, or data that cannot leave the machine, and who
currently choose between "capable but leaky" and "private but primitive."

Secondary: the local-AI enthusiast — but as a beachhead, not the destination.
Enthusiasts tolerate plumbing; professionals pay for the plumbing to
disappear. Aether 2.0's setup story (Wizard, Doctor, auto-tune) is what
carries it across that gap.

Aether is *not* for: people who want a free ChatGPT clone, or teams wanting
hosted collaboration. Do not chase either.

## What Aether 2.0 is

Three ideas, composed:

1. **A private knowledge substrate.** Unified memory (global / workspace /
   conversation scopes) plus RAG datasets plus workspace understanding merge
   into one thing users perceive: "Aether knows my projects." Knowledge is
   provenance-tracked — every answer can show which memory, chunk, or file it
   drew from.

2. **An accountable agent.** The read-first contract grows into approval-gated
   execution (commands, then MCP tools), but the constitution never changes:
   deterministic risk classification, review queues, traces, workspace
   boundaries. The pitch is not "an agent that can do anything" — everyone has
   that — it is "the only agent whose actions you can audit like a ledger."

3. **The machine's AI substrate.** A headless local API over the same core, so
   editors, scripts, and other tools use Aether's models, memory, and RAG
   instead of each app rebuilding (and each app separately leaking). Aether
   becomes infrastructure — the thing on the machine that other tools talk to
   — which is a far more durable position than being one more chat window.

## Against the field

- **Claude Desktop / ChatGPT:** superb models, zero sovereignty. Aether is
  provider-agnostic and can *use* those APIs, but the memory, history, and
  orchestration stay local. They are model vendors; Aether is the user's side
  of the table.
- **LM Studio:** a model runner with a chat UI; no memory, no agent, no
  project awareness, closed-source, and its story ends at "the model loaded."
  Aether starts where LM Studio ends.
- **Open WebUI:** a self-hosted web stack (Docker, Python, browser). Aether's
  thesis is native: one process, no server administration, OS-integrated
  (tray, hotkeys, credential store). The user is a person, not an ops team.
- **Continue / Cline / Roo Code:** editor extensions; they live inside VS Code
  and inherit its context model. Aether is workspace-level, editor-agnostic,
  and — via the local API — could be the backend those extensions point at.
- **OpenHands:** autonomous execution-first agents, maximum capability,
  minimum accountability, server-shaped. Aether is the deliberate opposite:
  capability grows only as fast as auditability.

The moat is not any feature — features are copyable. The moat is the
*posture*: native + local-first + auditable is a corner none of the
venture-scaled competitors can occupy without breaking their own model, and
that Aether has been consistently building toward from the first commit.

## What "fundamentally different" feels like in use

You open a project. Aether already knows it — its languages, its instructions,
its linked dataset, what you decided last month. You ask for a change; the
agent reads, drafts, and queues a patch with a diff and a reason; you approve.
You ask where a claim came from; it cites the chunk and the file. You open
Privacy Audit before a client call and see, in one screen, that nothing left
the machine this week. Your editor's AI assistant, unknown to itself, has been
talking to Aether the whole time.

Nothing in that paragraph requires inventing new AI. It requires finishing the
composition of what already exists — which is exactly what the 1.x→2.0 roadmap
(document 07) sequences.
