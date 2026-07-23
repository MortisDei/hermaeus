# 03. Lifecycle and glue

The attachment from doc 01 creates a reference from a conversation to a
dataset that can be deleted, reindexed, or renamed underneath it. This doc
keeps every such state honest, and adds the one navigation affordance that
makes the feature discoverable from the RAG side.

## 3.1 Picker refresh model: on open, nothing else

The chat header picker refreshes its dataset list every time its flyout
opens (one `GetDatasetsAsync` call; the table is tiny). There is no event
bus between RagViewModel and ChatViewModel, and this round must not invent
one. A dataset created or deleted while the flyout is closed is simply
picked up next open. State this in code with a one-line comment on the
refresh command so a future round does not "fix" it into event plumbing
without cause.

## 3.2 Deleted or missing dataset: degrade honestly, keep the id

When a conversation's `RagDatasetId` no longer resolves:

- Injection: skipped with the trace note per doc 02.2. Never an error
  dialog, never auto-clearing the stored id (the dataset may be on a
  temporarily unmounted data root; silently forgetting the attachment is
  the same sin as auto-removing missing sources, which r10 explicitly
  banned).
- Picker button: shows "Knowledge: missing" (tooltip: the stored id and
  "the attached dataset no longer exists; pick another or None"). The
  flyout list offers the normal choices; picking None or another dataset
  overwrites the stale id, which is the only way it changes.
- Dataset deletion in the Dataset Manager does NOT scan conversations or
  warn "N conversations reference this". That coupling is not worth its
  cost; the picker and trace honesty above are the contract.

## 3.3 "Open in chat" from the Dataset Manager

Each dataset card in the RAG panel's Dataset Manager gains an
**Open in chat** action:

- Navigates to the Chat view and starts a new conversation with
  `RagDatasetId` pre-attached to that dataset (and the picker showing its
  name). Reuse the existing navigation glue: `MainWindowViewModel` owns
  view switching and ChatViewModel access (verify how the tray/hotkey
  "new conversation" path (Ctrl+N) creates one and reuse that code path,
  then set the dataset and save).
- If a draft is sitting unsent in the current chat input, this action must
  not destroy it; the existing new-conversation path's behaviour is the
  contract, whatever it is (verify and do not change it).
- No reverse affordance ("open dataset" from chat) this round.

## 3.4 Privacy Audit disclosure

`PrivacyAuditService` has a "Features that may send data remotely" section
(PrivacyAuditService.cs:172) which r19 taught about image attachments under
a remote chat provider. Add the equivalent line for RAG injection: when any
conversation could inject local document excerpts and the selected chat
provider is remote (OpenAI-compatible non-localhost), disclose
"Chat knowledge context: excerpts from local RAG datasets are included in
prompts sent to the remote chat provider." Match the detection style the
image-attachment entry uses (verify how it decides "remote provider" and
reuse the same helper). The entry appears when the capability is live (RAG
subsystem available and a remote provider selected), not only when a
dataset is currently attached; the audit describes surface, not the current
toggle state, matching how the image entry behaves.

## 3.5 Docs to update (user-visible behaviour)

- `docs/features.md` Chat section: the Knowledge picker, injection
  behaviour, weak-retrieval skip, citation pills, trace fields, Context
  Inspector part. RAG section: BM25-only embedding-failure fallback,
  Open in chat. Correct the existing sentence that implies RAG citations
  already render under chat replies (it described infrastructure, not
  behaviour; as of this round it becomes true, so rewrite it to describe
  the real flow).
- `docs/rag.md`: new "Using a dataset in Chat" subsection (attachment,
  injection block shape, weak-retrieval skip, budget setting, missing
  dataset behaviour); Querying section gains the embedding-failure
  fallback note.
- `docs/security-review.md`: r21 subsection. New surface: none over the
  network; the changes move existing local dataset content into chat
  prompts (remote-provider implication covered by 3.4), and the
  conversation store gains one additive column. State that injection
  content is bounded by the token budget and that the weak-retrieval gate
  prevents indiscriminate corpus leakage into unrelated remote-provider
  chats. Note 2.1 removes a raw exception path but adds no new privilege.
- `CHANGELOG.md` 0.27.0-alpha entry (respect the 10-version FIFO; archive
  the oldest entry to docs/changelog-archive.md).
