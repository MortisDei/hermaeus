# 04. Voice: clipping, punctuation, pickers, and a stop button

## 4.1 Word clipping: split phoneme chunks at boundaries, not mid-word

Owner: "the voice still has clipping on words". Strong code-verified suspect:
`KokoroTokenizer.Encode` (`src/Aether.Voice/KokoroTokenizer.cs:31-40`) splits a long
phoneme stream into context-window chunks at a HARD offset
(`offset += KokoroVocab.MaxSequenceTokens`), with no awareness of word or sentence
boundaries. Any utterance whose phonemes exceed one window gets chopped mid-word;
`NativeKokoroVoiceProvider` then synthesizes each chunk as a standalone utterance and
concatenates the samples back to back (`NativeKokoroVoiceProvider.cs:209-212`), so the
seam lands inside a word: the audible artifact is a clipped/garbled word part-way
through long responses, exactly the owner's report.

Fix:
- Make `Encode` boundary-aware: prefer to break at the last sentence punctuation
  (`.`/`!`/`?`), then clause punctuation (`,`/`;`/`:`), then the last space token,
  within the window; only fall back to the hard cut when a single unbroken run exceeds
  the window (pathological input). The phoneme string contains these characters today
  (the phonemizer appends trailing punctuation per word, `KokoroPhonemizer.cs:44-58`,
  and word separators survive normalization); verify with a quick probe before coding
  against it.
- Insert a short silence between chunks when stitching in
  `NativeKokoroVoiceProvider` (about 120 ms of zero samples at the model sample rate
  after a sentence-boundary split, about 60 ms after a clause/space split) so seams
  read as pauses, not splices.

Acceptance: unit tests on `Encode`: a phoneme string longer than one window with a
sentence break inside the window splits at the break (chunk 1 ends with the
punctuation token); an unbroken run still splits and never produces an oversized
chunk; total non-pad token count is preserved across chunks. A stitching test that the
combined sample count equals the sum plus the inserted silence.

## 4.2 Punctuation adherence pass

Owner: "not adhering to punctuation properly, better but still needs work". Two
code-visible gaps to close first, then re-evaluate by ear:

- Paragraph and line breaks vanish: `KokoroPhonemizer.Phonemize` splits on all
  whitespace (`KokoroPhonemizer.cs:28`), so `\n\n` between paragraphs contributes no
  pause at all. Map a paragraph break to a sentence-pause token before splitting
  (inject `.` when the preceding character is not already sentence punctuation, or
  carry an explicit pause marker through to the stitcher).
- Only TRAILING `. , ! ? ; :` per word survive (`IsSentencePunctuation`, :61). Quotes,
  parentheses, and leading punctuation are dropped, which is mostly correct for Kokoro,
  but em/en dash and ellipsis normalization already downgrades to `,`/`...` in
  `KokoroTextNormalizer` (:73-113); verify those actually reach the phoneme stream as
  vocab tokens (the tokenizer silently drops any char not in `KokoroVocab.SymbolToId`,
  `KokoroTokenizer.cs:27-28`). Add a debug-level log counting dropped characters per
  utterance so future adherence bugs are observable instead of guessed at.

Acceptance: phonemizer tests: two-paragraph input yields a sentence-pause token at the
break; "wait... what?" retains the pause tokens end to end (assert on the encoded id
sequence containing the punctuation ids, constructed via `KokoroVocab.SymbolToId`, not
via literals, to respect the no-unicode-glyphs rule).

## 4.3 Voice orchestration: dropdowns, not free text

Owner request, verified in `src/Aether.Desktop/Views/SettingsVoiceSectionView.axaml`:
- Channel "Profile" is a raw TextBox (:112) that must match a profile name by string.
- Profile "Provider voice id" is a raw TextBox (:131).

Fix:
- Channel profile: replace with a ComboBox whose ItemsSource is the defined profile
  names plus a leading "(Default voice)" entry mapping to empty string. Source it from
  the same `VoiceProfiles` collection the section VM already edits; keep it live as
  profiles are added/renamed/removed (recompute on collection change; a stale name
  after a rename should surface as unselected, not silently kept).
- Voice id: replace with an editable ComboBox (`IsEditable`) listing the active
  provider's voices. Kokoro native already has the canonical list
  (`NativeKokoroVoiceProvider.SupportedVoices`, :17-19); expose available voices
  through the existing `ITtsService`/provider seam (add
  `GetAvailableVoicesAsync` to the voice-provider contract if no equivalent exists,
  returning an empty list for providers that cannot enumerate, which keeps the box
  editable free-text for those).
- Speed stays a NumericUpDown.

Acceptance: VM test that channel options = profiles + default entry and update on
profile add/remove; provider fake returning three voices populates the voice list;
providers returning empty leave manual entry working.

## 4.4 A visible way to stop the voice

Owner: no way to stop playback; the speak icon should become a stop icon while
playing. The backend already exists: `IVoiceOrchestrator.StopChannel`/`StopAll`
(`src/Aether.Core/Services/IVoiceOrchestrator.cs:14-15`) and `ChatViewModel` even
calls `StopChannel` before speaking (:783). What is missing is state + UI:

- Add `event Action<VoiceChannel>? UtteranceCompleted` (fired on finish, stop, and
  failure, from the orchestrator worker loop's finally) and a thread-safe
  `bool IsSpeaking { get; }` to `IVoiceOrchestrator`/`VoiceOrchestrator`. Started
  already exists (`UtteranceStarted`, :17).
- `ChatViewModel`: `IsVoicePlaying` observable driven by those events (marshaled via
  `RunOnUi`). The per-message speak button in `MessageControl.axaml` (the action row
  around :109) swaps icon and command: speak -> `SpeakMessageCommand`, playing ->
  stop icon invoking `StopSpeakingCommand` (`StopChannel(VoiceChannel.Chat)`).
- Add one global stop in the chat header area, visible only while `IsVoicePlaying`,
  for streamed auto-speech (which queues many chunks; StopChannel already clears the
  channel queue, verify and cover with a test).

Acceptance: orchestrator test: enqueue then StopChannel fires UtteranceCompleted and
empties the queue, IsSpeaking false afterwards; VM test that the flag tracks the two
events.
