# Pull Requests

Changes to `main` land through pull requests. This keeps review in front of
the release flow described in [`packaging.md`](packaging.md).

## The one rule that is not negotiable

There is one open pull request per maintainer at a time. Finish or close the
existing one before opening another.

## Workflow

1. Branch from up-to-date `main`. Use `rNN/<topic>` for review-round work,
   `fix/<topic>` for a hotfix, or `docs/<topic>` for documentation-only work.
2. Keep one deliverable per branch. Unrelated fixes get a separate branch.
3. Commit with a Conventional Commit-style subject. Do not add AI co-author
   trailers. Non-trivial commits include why the change is correct, relevant
   security or privacy semantics, verification performed, and deliberate
   limitations.
4. Before opening the PR, run `dotnet build Hermaeus.sln` with zero warnings,
   run the documented test suite, and check that the documentation and
   `CHANGELOG.md` are truthful.
5. Open the PR against `main` using [the pull request template](../.github/PULL_REQUEST_TEMPLATE.md).
   CI must pass.
6. Use a squash merge for fix and documentation branches. Use a merge commit
   for review-round branches when their commit sequence is part of the record.
   Delete the branch after merge.

Tags and GitHub releases are owner actions after the merge. They are never
created or pushed from a documentation branch.

## Scope and review

A review round may land as one PR or a small sequential series. Its active
roadmap decides the sequence. A hotfix may be pushed directly only when `main`
is broken; record that exception in the commit and open a follow-up review.

Self-review is still real review for a solo maintainer. Read the complete diff
on the hosting service before merging, and complete every applicable checklist
item in the PR template. A documentation change must preserve current
authority, keep historical review records intact, and leave no planned feature
described as shipped.

For review-round development pushes matching `r*/round`, `branch-ci.yml`
provides non-required Linux and Windows feedback until an exact open
same-repository pull request exists. The required `build-and-test` checks are
published by `ci.yml` for the pull-request merge context and `main` pushes.
The branch workflow never reuses those required check names.

For test execution, coverage, platform skips, and the evidence behind the
Windows CI timing guidance, use the [test-suite reference](testing.md). It is
the canonical home for that material and avoids duplicating measurements here.
