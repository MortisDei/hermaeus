# 03 - Model catalog clutter, HF browser scrolling, chat memory disclosure

## 3.1 Hugging Face search results list doesn't scroll

`ModelManagementView.axaml`, "Get models from Hugging Face" `Expander`
(line 65): the search results `ItemsControl` at line 88 has a hard
`MaxHeight="180"` but is not wrapped in a `ScrollViewer` - unlike the
main local-models list, whose `ItemsControl` is correctly wrapped in a
named `ScrollViewer` at line 152. With more results than fit in 180px,
content clips instead of scrolling: this is the reported "can't scroll
down to see all search options." The per-model file list (`HfFiles`,
line 108) has no height cap or scroll wrapper at all, so a repo with
many files (sharded GGUFs, multiple quantizations) can push the whole
expander arbitrarily tall instead of scrolling internally. Wrap both
`ItemsControl`s in `ScrollViewer`s the same way the main list already
does; keep `HfSearchResults`' `MaxHeight` (or pick a slightly taller
one, e.g. 260-320px) as the `ScrollViewer`'s height, and give
`HfFiles` a matching cap since it's currently unbounded.

## 3.2 Small/orphan entries in the local models list

Confirmed not caused by non-GGUF sidecar files: `LocalAiAssetLocator.FindGgufModels`
(`LocalAiAssetLocator.cs:25-42`) filters strictly to `*.gguf` via
`Directory.EnumerateFiles`, so `tokenizer.json`/`config.json`/`.txt`
files from an HF download do not surface as model rows. The likely
actual cause: sharded GGUF downloads (`model-00001-of-00003.gguf`,
`model-00002-of-00003.gguf`, ...) are common for larger models, and
`FindGgufModels` lists every matching file individually with no
shard-awareness - each shard becomes its own row in
`ModelManagementViewModel.Models` (`ModelManagementViewModel.cs:87-101`,
`:135-153`), which explains "smaller model files < ~500 MB" cluttering
the list: they're fragments of a larger model, not independent models,
and only the first shard (`-00001-of-`) is directly loadable by
llama.cpp (it reads the rest via the naming convention).

Before building shard-grouping UI, verify against the user's actual
model directory which pattern is present (sharded GGUFs, or something
else like duplicate re-downloads) - do not guess further. If it is
shard fragments:
- Group rows whose filename matches
  `^(?<base>.+)-(?<part>\d+)-of-(?<total>\d+)\.gguf$` into one logical
  model entry keyed on `base`+`total`, sized as the sum of all parts,
  and only expose the `-00001-of-` path as the loadable `ModelPath`.
  Do this at the `DiscoverLocalGgufModels`/`FindGgufModels` boundary so
  every downstream consumer (fit estimator, context suggestions, Auto
  Tune) sees one entry, not N.
- If instead there's a non-shard explanation, document the actual
  finding here and scope a targeted fix instead of the shard-grouping
  above.
- Whatever ships, the Models page must make it obvious a size <500 MB
  GGUF entry is a fragment, not a full model, until it's confirmed
  clustered - a silent tiny "model" the user can select and fail to
  load is worse than an unexplained list.

## 3.3 Chat memory disclosure: show a count, not every memory

There is no per-memory list in chat today, contrary to how it reads in
use: `ChatViewModel.MemoryStatus` (`ChatViewModel.cs:1334-1362`) is
already a single collapsed summary string ("Memory on - N recent" /
"Memory on - N in this chat - N recent"), bound as one `TextBlock` in
`ChatView.axaml:228-231`. What the user is actually seeing "all the
memories loaded" as is the per-message **Sources** panel: recalled
memories are added to the assistant message's `Sources` collection
(`ChatViewModel.cs:467-469`, `asst.Sources.Add(source)`, fed from
`BuildMemoryInjectionAsync`, `ChatViewModel.cs:969-1021`), and
`MessageControl.axaml:55-74` renders every `SourceReference` - RAG
citations and injected memories alike - as an always-visible pill
under the message, unconditionally, with no count-first collapse.

Fix: separate the two source kinds in the pill display, or at minimum
collapse memory pills behind a summary the same way `MemoryStatus`
already collapses the header line:
- Give `SourceReference` (or the `Sources` collection) a way to
  distinguish memory-sourced entries from RAG-citation entries (a
  `Kind`/`IsMemory` flag set where `memorySources` are added,
  `ChatViewModel.cs:468-469`), since RAG citations are a different use
  case (the user wants to see and click into document chunks) from
  memory recall (the user mostly wants a background - "did this use my
  memories, and can I check").
- In `MessageControl.axaml`, keep RAG citation pills as they are today
  (individually visible, clickable), but render memory-sourced entries
  as a single "Memories used: N" pill that expands the individual
  memory pills on click (a simple `ToggleButton`/`IsExpanded` bound
  local flag is enough - no new service needed).

## Acceptance

- HF search results and per-model file lists both scroll internally
  inside the expander instead of clipping.
- Local models list either groups sharded GGUFs into one entry per
  model or the sub-500MB clutter is explained by a different confirmed
  cause and fixed accordingly.
- Chat messages show a collapsed "Memories used: N" indicator that
  expands on click instead of N always-visible pills; RAG citation
  pills are unaffected.
