# 02. One context receipt

## The bug, exactly

The owner's report: with the memory pill collapsed, the memories used were
still listed above it. Clicking the pill shows the memories used, which is
the correct behaviour. So the pill works and something else is leaking.

The chain, verified at `1bd3f2d`:

1. r18 3.3 collapsed memory-sourced pills behind a single count, because
   one always-visible pill per recalled memory "read as all the memories
   loaded". The collapse lives at `MessageControl.axaml:105-116` and the
   split that feeds it at `MessageViewModel.cs:83-96`.
2. That split tests one kind: `if (source.Kind == ProvenanceKind.Memory)`
   goes to `MemorySources`, and **everything else falls through to
   `CitationSources`** (`MessageViewModel.cs:89-93`).
3. r24 2.6 added Recall injection. Its sources are deliberately tagged
   `ProvenanceKind.Recall` rather than `Memory`, so a `[MEMORY_UPDATE]`
   marker can never target one (`ChatViewModel.cs:1551-1560`). That
   reasoning is correct and must be preserved.
4. `CitationSources` renders as an always-visible strip of pills at
   `MessageControl.axaml:82-101`, positioned directly above the collapsed
   memory pill.

Recall hits are past messages, agent tasks and memories from this machine.
To the person reading the screen they are memories. They render, expanded,
immediately above a control claiming to have collapsed them.

`ChatRecallInjectionTests.cs:67-68` asserts this placement, so the fix
changes that test's intent rather than only its code. Its real invariant is
the `Recall`-not-`Memory` tagging, and that stays.

## Do not fix it by adding `|| Kind == Recall`

That yields two collapsed pills that both say "used", disagree about what
they counted, and leave RAG excerpts as a third, differently-behaved strip.
Three source kinds, three presentations, no total.

The Agent panel already solved this shape.
`AgentContextReceiptBuilder.Build` (`src/Hermaeus.Agent/Services/AgentContextReceiptBuilder.cs:19-40`)
turns a context pack into labelled sections with a count and a token
estimate each, omitting empty sections, rendered at
`AgentView.axaml:627-640`. Chat should have the same thing, because it is
answering the same question: what went into this answer.

## 2.1 `ChatContextReceipt`

A pure builder in `Hermaeus.Core`, over `IReadOnlyList<SourceReference>`:

```
ChatContextReceiptSection(Kind, Label, ItemCount, EstimatedTokens, Items)
ChatContextReceipt.Build(IReadOnlyList<SourceReference>) -> sections
```

- Deterministic section order: Memory, Recall, Knowledge, Workspace, then
  anything else in enum order. Ordering by dictionary iteration is not
  ordering.
- Empty sections are omitted, matching `AgentContextReceiptBuilder`'s
  `AddSection` behaviour.
- Labels are plain and plural-correct: "Memories", "Recall", "Knowledge
  excerpts".
- Token estimates reuse the estimator chat already uses; do not add a
  second one.

Pure function, no view model, no store. Most of this doc's tests live here.

## 2.2 One receipt in the message

Replace both the citation strip (`MessageControl.axaml:82-101`) and the
memory pill block (`:105-152`) with a single expander:

```
Context: 3 memories, 2 recall hits, 4 knowledge excerpts        (collapsed)
```

- Collapsed by default, and **collapsed means nothing from any section is
  visible**. That sentence is the entire point of this doc; if the
  implementation leaves any pill outside the expander, it has not fixed the
  reported bug.
- Expanded shows each section with its own pills.
- The memory pill's flyout (title, snippet, "Open in Memories", wired to
  `OpenMemoryCommand` at `MessageControl.axaml:123-150` and
  `ChatViewModel.cs:284`) moves into the receipt unchanged. It works today
  and is not part of the bug.
- Hidden entirely when there are no sources, so an ordinary turn with no
  memory, no recall and no dataset gains no new chrome. Most turns are
  ordinary turns.

Note that this lands in the same footer region as doc 01's branch
switcher. Doc 06 sequences this doc first for that reason.

## 2.3 Fix `HasSources` while you are here

`MessageControl.axaml:83` binds the citation strip's visibility to
`HasSources`, which is true when **any** source exists, including a turn
whose only sources are memories (`MessageViewModel.cs:84`). Today that
renders an empty `ItemsControl`, which is invisible and harmless. Once the
receipt owns this region, bind visibility to the receipt having sections,
not to `HasSources`. Leaving a stale boolean driving new layout is how the
next version of this bug gets written.

## 2.4 Keep inline citation markers

The model emits `[1]`, `[2]` markers that refer to packed chunk order, set
up by the injected prompt text (`ChatViewModel.cs:1483-1489`). Those stay
exactly as they are.

The receipt answers "what went in". The markers answer "what this sentence
leaned on". They are different questions and collapsing one into the other
loses information. Do not remove or renumber the markers to tidy the
footer.

## Testing

Roughly 14 to 18.

Builder: section order is stable regardless of input order; empty sections
omitted; counts correct per kind; a Recall source lands in the Recall
section and never in Knowledge; token estimates present and non-negative;
an empty input produces an empty receipt.

View model: a turn with only memory sources produces one section; a turn
with memory plus recall plus RAG produces three in the fixed order; a turn
with no sources produces a receipt that reports itself empty so the view
can hide.

Regression, stated as the owner's report: a message carrying both memory
and recall sources exposes **no** visible source item while the receipt is
collapsed. Write this one first and watch it fail against current
behaviour before changing anything.

Update `ChatRecallInjectionTests.cs:60-70` to assert the surviving
invariant (recall sources are tagged `ProvenanceKind.Recall`, never
`Memory`, and are never added to `injectedMemoryIds`) without asserting
that they render in the citation strip.
