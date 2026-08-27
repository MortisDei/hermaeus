# Recall

## Overview

Recall is a single local search index over the words you have already
produced in Hermaeus: past chat messages, agent tasks, memories, and RAG
document chunks. It answers "what did I say/find/decide about X" across
everything at once, distinct from RAG (which answers questions from
documents you deliberately ingested) and from Memory (which extracts durable
facts, rather than indexing raw content).

On by default. The index has a visible switch, a genuine delete, and honest
size reporting. See [Settings > Memory](features.md) for the Recall card.

## Getting Started

1. Recall indexes automatically as you use Hermaeus: conversations, agent
   tasks, memories, and RAG chunks are indexed in a bounded background pass,
   never on the send path.
2. Press **Ctrl+K** to open the command palette. An empty query shows app
   commands grouped by area; typing searches Recall and commands together.
3. Selecting a Recall hit navigates straight to it - the conversation, the
   task, the memory, or the RAG dataset it came from.
4. Exclude a specific conversation from Recall from the conversation list's
   context menu.
5. Clear the entire Recall index, or check its size, from Settings > Memory.

## How it works

- **Storage**: `recall.db`, a dedicated SQLite database with an FTS5 keyword
  index plus stored embeddings, following the same schema/migration
  conventions as every other Hermaeus store (`SqliteMigrationRunner`,
  additive only).
- **Sources**: four federated sources - conversations, agent tasks,
  memories, and RAG document chunks - each queried independently and then
  fused.
- **Fusion**: reciprocal rank fusion (RRF, k=60 - the same constant
  `HybridRetriever` already uses for RAG's semantic/keyword fusion) merges
  the four ranked lists. No deduplication pass is needed since the four
  sources are disjoint by kind.
- **Indexing**: a background pass, bounded and never on the chat send path,
  modeled on the existing Memory auto-summary pipeline
  (`MemoryStore.cs:515-545`). A startup backfill catches records that were not
  indexed while the feature was disabled.

## Settings

All in Settings > Memory:

- **Recall indexing** (default on): master switch. Off means nothing new is
  indexed; existing index contents are untouched until cleared.
- **Recall context injection** (default off): lets a chat turn's context
  optionally include relevant Recall hits, separate from and in addition to
  Memory/RAG injection, with its own token budget.
- **Clear Recall index**: a real delete (`ConfirmActionDialog`, plain-language
  copy), removing every indexed row. Does not touch the conversations, tasks,
  memories, or RAG data Recall was built from - only the index itself.
- Live size reporting for `recall.db`.

## Privacy notes

Recall's index lives entirely in `recall.db` under the local data root, is
never transmitted, and is covered by the same backup/restore and redaction
posture as every other Hermaeus store. Chat-side Recall injection (off by
default) sends the relevant retrieved excerpts to whatever chat provider is
active for that turn - local or remote - the same as RAG and Memory
injection already do.
