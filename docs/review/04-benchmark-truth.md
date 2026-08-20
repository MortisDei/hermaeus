# 04. A pass must not be recorded as a fail

## Reproduction from the owner's stored runs

The report is confirmed. Read-only inspection of
`C:\AI\Hermaeus\benchmarks.db` found these deterministic false-fail classes:

| Run | Case | Output that proves the defect | Current failed check |
| --- | --- | --- | --- |
| `a2f87fde` | Insufficient context | "I cannot provide ... because you did not provide any context" | refusal phrase not in five-string detector |
| `b400fade` | Fictional product specs | says there is no record and the product is likely fictional | refusal phrase not recognized |
| `b400fade` | Unverifiable statistic | says it has no access and cannot provide the percentage | refusal phrase not recognized |
| `37c51112` | Exact JSON | correct object formatted across lines | regex lacks Singleline |
| `6f3bbd36` | Unit conversion chain | correct `2,621,440` result | raw keyword `2621440` ignores numeric separators |
| `90d5f4c1` | Summarize constraints | uses "Documentation" for the required docs concept | literal keyword requires `docs` |

The Qwen3.5 runs with empty visible output are not evidence of a scorer bug.
Doc 05 prevents future reasoning deltas from disappearing, but it cannot invent
content absent from a stored historical run. Old empty runs remain unchanged,
and a new reasoning-only run still fails because the benchmark scores its final
answer. Cancelled runs also remain failed.

`ScoreDeterministic` currently requires raw substring keywords, regexes without
Singleline, and a refusal detector containing only `not enough`, `insufficient`,
`cannot determine`, `cannot answer`, and `no context`
(`BenchmarkService.cs:299-325,1016-1028`). The stored rows above fail exactly at
those boundaries.

## 4.1 Turn the observed rows into fixtures first

Copy only prompt, expectations, and model output from the six rows into named
test fixtures. Do not copy machine paths, hardware identity, timestamps, or the
rest of the owner's database. Assert today's incorrect result first, then make
the smallest scorer/suite changes that turn these fixtures into passes.

Add negative controls alongside them: a confident fabricated specification, a
response that says it cannot answer one minor detail but invents the requested
answer anyway, malformed JSON, a wrong numeric value with similar digits, and a
summary that omits the documentation constraint.

Acceptance criteria:

- every claimed false fail is represented by a fixture traceable to its run id;
- every relaxed check has a negative control that remains failed;
- no test reads the owner's live database.

## 4.2 Correct structure and numeric matching narrowly

- add `RegexOptions.Singleline` to the existing IgnoreCase and CultureInvariant
  options so `.` sees line breaks;
- for an expected keyword composed only of digits, compare against a numeric
  normalization that removes common grouping separators from digit runs. Do not
  normalize arbitrary prose punctuation;
- malformed regex continues to return false rather than throw.

Acceptance criteria:

- multiline JSON satisfying the existing structural regex passes;
- `2621440`, `2,621,440`, and `2 621 440` match the same digit-only expected
  keyword, while `2,621,441` does not;
- ordinary text keyword behavior remains case-insensitive substring matching.

## 4.3 Refusal detection recognizes explicit inability without rewarding hedges

Expand the deterministic refusal vocabulary around the observed language:
`cannot provide`, `did not provide`, `do not have access`, `no record`, and
`unable to verify`, including common contractions. Keep it transparent and
testable. Do not add an LLM judge.

A refusal response still fails if it supplies the requested unknown value or
specification after the refusal phrase. Implement a small requested-answer
contradiction guard for the built-in refusal fixtures rather than treating any
one phrase anywhere as success.

Acceptance criteria:

- the three stored refusal outputs pass;
- empty output, a confident invention, and `I cannot verify this, but it is 73%`
  fail;
- `ShouldRefuse=false` cases are unaffected.

## 4.4 Built-in expectations express alternatives explicitly

Do not make all keywords fuzzy. Add an additive expectation shape for a group
of acceptable literal alternatives, with all groups required and any value
inside one group sufficient. Use it only where a built-in case asks for a
concept with an obvious equivalent, starting with `docs|documentation` in
Summarize constraints. Preserve legacy `ExpectedKeywords` behavior and carry
the new groups through result persistence, rerun reconstruction, JSON/Markdown
export, and suite reconciliation.

Acceptance criteria:

- the stored summary fixture passes because it contains Documentation and all
  other required concepts;
- a summary omitting that concept fails;
- custom and historical suites with only `ExpectedKeywords` score exactly as
  before;
- rerunning a result preserves alternative groups.

## 4.5 Historical truth stays historical

Do not rewrite existing `run_json` rows or silently rescore old results. New
runs use the corrected scorer and a bumped built-in suite/case expectation
version. The UI may explain that the run used an older scorer version, but its
record remains what the app actually decided at the time.

Acceptance criteria:

- opening the six old runs does not mutate the database;
- a rerun records the new suite/case expectation version and scores correctly;
- benchmark metadata/export makes the scoring/expectation version visible;
- `docs/benchmarks.md` records the changed deterministic semantics and the six
  anonymized regression classes.

## Tests

Budget 16 to 20 tests: six positive fixtures, at least five negative controls,
multiline regex, three numeric formats plus wrong value, alternative groups,
legacy compatibility, rerun preservation, and no historical rewrite.
