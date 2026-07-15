# 01 - Voice: pronunciation you can trust

## Problem statement

`KokoroPhonemizer` (src/Aether.Voice/KokoroPhonemizer.cs) is a ~250
word lookup table plus letter-by-letter fallback rules. Two rounds of
hand-tuning (`ad618da` Magic E + digraphs, `aea2326` rhotics +
tion/sion) improved it and pronunciation is still not acceptable. This
is not a tuning problem: English spelling-to-sound is irregular enough
that rules over raw letters cannot reach acceptable quality. The fix is
a real pronouncing dictionary, with the rules demoted to
out-of-vocabulary fallback.

Two outright bugs make it worse than the rules suggest:

- **Digits are dropped entirely.** `MapLetter`
  (KokoroPhonemizer.cs:165-181) maps any non-letter to
  `string.Empty`, and `KokoroTokenizer.Encode`
  (KokoroTokenizer.cs:25-29) silently skips characters not in the
  vocab. "You have 3 errors" speaks as "You have errors". "$5" is
  silence.
- **Duplicate dictionary keys.** `["new"]` is assigned twice
  (KokoroPhonemizer.cs:428 "nu", then :449 "nju"; the second silently
  wins via indexer semantics) and `["may"]` twice (:311 modal, :392
  month). Harmless at runtime but evidence the table has no tests.

## 1.1 Text normalization pass

New pure static class `KokoroTextNormalizer` in `Aether.Voice`, applied
inside `NativeKokoroVoiceProvider` before
`KokoroPhonemizer.ToPhonemes` (call site
NativeKokoroVoiceProvider.cs:190). Runs after
`ChatSpeechSanitizer.Sanitize` (which strips markdown at the
ViewModel layer, src/Aether.Core/Services/ChatSpeechSanitizer.cs) and
must not assume sanitized input, because `speak` can be invoked with
arbitrary text.

Scope (each expands to plain English words):

- Cardinal integers with optional thousands separators: `1,234` ->
  "one thousand two hundred thirty four". Support up to the billions;
  beyond that, read digit by digit.
- Decimals: `3.14` -> "three point one four".
- Negatives: `-4` -> "minus four" (only when directly prefixing a
  number).
- Ordinals: `1st`, `2nd`, `3rd`, `21st` -> "first", "second",
  "third", "twenty first".
- Percent: `85%` -> "eighty five percent".
- Currency: `$5`, `$5.20` -> "five dollars", "five dollars twenty
  cents". Dollars only this round.
- Clock times: `3:30` -> "three thirty"; `3:05` -> "three oh five".
- Standalone symbols between words: `&` -> "and", `+` -> "plus",
  `/` -> "slash", `@` -> "at", `=` -> "equals".
- All-caps tokens of 2-6 letters not found in the lexicon (1.2) are
  spelled letter by letter: `GGUF` -> "g g u f", `API` -> "a p i".
  Tokens found in the lexicon (e.g. `NASA` if present) are spoken as
  words.

Everything else passes through unchanged. No year-style reading of
four-digit numbers this round (1999 is "one thousand nine hundred
ninety nine"); recorded as a rejection in doc 05.

**Acceptance criteria**

- Pure function, no I/O, deterministic; unit tests cover every bullet
  above with at least one positive and one boundary case each.
- `NativeKokoroVoiceProvider` output for "You have 3 errors" contains
  the phonemes for "three" (test via `ToPhonemes` on the normalized
  string; no audio assertion).
- Normalization is applied for both the streaming chat-speech path and
  direct `speak` calls (single call site inside the provider
  guarantees this).

## 1.2 CMUdict-backed lexicon

Ship the CMU Pronouncing Dictionary (cmudict-0.7b, BSD 2-clause,
~134k entries) as a gzip-compressed embedded resource in
`Aether.Voice` (roughly 1 MB compressed). Add the license text under
`src/Aether.Voice/Assets/` and a THIRD-PARTY-NOTICES entry at repo
root (create the file if absent).

- Loader: lazy, thread-safe, on first synthesis; parses ARPABET with
  stress digits into IPA strings compatible with `KokoroVocab`.
  Multiple entries per word (heteronyms): take the first (most common)
  entry; alternates are out of scope this round.
- ARPABET -> IPA mapping is a small static table (AA->ɑ, AE->æ,
  AH0->ə, AH->ʌ, AO->ɔ, AW->aʊ, AY->aɪ, CH->ʧ, DH->ð, EH->ɛ,
  ER->ɚ, EY->eɪ, HH->h, IH->ɪ, IY->i, JH->ʤ, NG->ŋ, OW->oʊ,
  OY->ɔɪ, R->ɹ, SH->ʃ, TH->θ, UH->ʊ, UW->u, Y->j, ZH->ʒ, G->ɡ,
  remainder map to themselves lowercased). Stress: emit `ˈ`
  immediately before the vowel carrying stress digit 1, `ˌ` before
  digit 2. The implementer must confirm every emitted character exists
  in `KokoroVocab.SymbolToId`; a unit test enumerates the mapping
  table and asserts vocab membership (this is what prevents the
  silent-drop failure mode in `KokoroTokenizer.Encode`).
- Lookup order in `KokoroPhonemizer.ToPhonemes`: user lexicon (1.3)
  -> CMUdict -> morphological retry (1.4) -> existing rule fallback.
  The existing ~250-entry inline dictionary is deleted; CMUdict
  covers all of it (remove the duplicate-key bug with it).
- Memory budget: parsed dictionary must stay under ~40 MB managed; if
  the naive `Dictionary<string,string>` exceeds it, store ARPABET
  bytes and convert on lookup. Measure once in a test with
  `GC.GetTotalMemory` before/after load (soft assertion, log only).

**Acceptance criteria**

- "voice", "choice", "colonel", "Wednesday", "queue", "ghost"
  phonemize to their CMUdict pronunciations (golden tests; "ghost"
  today becomes "fost" via the `gh->f` rule, which this fixes).
- First synthesis after app start completes without visible UI stall
  (load happens on the synthesis worker, not the UI thread).
- No behavior change for the Python Kokoro provider (`KokoroVoiceProvider`
  in Aether.Services does its own G2P and is untouched).

## 1.3 User override lexicon

Plain-text file `{DataRoot}/voice/lexicon.txt`, one entry per line,
`word = ipa` (IPA in Kokoro vocab symbols), `#` comments. Loaded with
the same lazy loader; reloaded when the file's mtime changes (checked
per synthesis call, cheap stat). Invalid lines are skipped with one
Warning runtime-log entry naming the line number.

Ship defaults for app-domain words CMUdict lacks, written at first
synthesis if the file does not exist: `aether = ˈiθɚ`,
`ollama = oʊˈlɑmə`, `kokoro = koʊˈkoʊɹoʊ`, `llama = ˈlɑmə`,
`qwen = kwɛn`. (The app currently mispronounces its own name.)

**Acceptance criteria**

- Override beats CMUdict (test: redefine "voice", assert override
  IPA is used).
- Characters outside `KokoroVocab.SymbolToId` in an override line make
  that line invalid (skip + warn), never silence.
- Settings > Voice gains an "Open pronunciation lexicon" button
  (opens the file with the OS default editor via the existing
  open-path helper used by the r6 data-root buttons) with a tooltip
  explaining the format.

## 1.4 Morphological retry and fallback cleanup

Before falling back to letter rules for an out-of-vocabulary word:

- Strip possessive `'s` / trailing `s` / `es` / `ed` / `ing` and retry
  the lexicon; on success append the correct suffix phonemes
  (s -> `z` after voiced, `s` after voiceless, `ɪz` after sibilants;
  ed -> `d`/`t`/`ɪd` by the same voicing rule; ing -> `ɪŋ`).
- In the remaining rule fallback, fix `gh` at word start to `ɡ`
  (currently `f` unconditionally, KokoroPhonemizer.cs:155).

**Acceptance criteria**

- "servers", "wanted", "running", "aether's" resolve via lexicon +
  suffix, not letter rules (tests assert the exact IPA).
- Rule fallback still handles a nonsense word without throwing.

## 1.5 Golden pronunciation regression set

One test file with ~30 sentences exercising: digits, currency, times,
ordinals, acronyms, heteronym-adjacent words, contractions, the user
lexicon defaults, and plain prose. Each asserts the full IPA output of
normalize + phonemize. This is the voice equivalent of the r7 scenario
suite: any future phonemizer change must consciously update the
goldens.

**Acceptance criteria**

- A second test asserts that for every golden sentence, zero
  characters are dropped by `KokoroTokenizer.Encode` (encode the IPA,
  compare token count to non-space vocab-symbol count).
