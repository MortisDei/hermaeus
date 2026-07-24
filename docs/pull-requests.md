# Pull Requests

Starting with the r23 implementation, changes to `main` land via pull
request instead of direct pushes (the commit introducing this policy and
the r23 spec pack is the last direct push). Tags and GitHub Releases already flow from `main`
(`docs/packaging.md`); PRs put a review gate in front of that flow.

## The one rule that is not negotiable

**One open pull request per maintainer at any one time.**

Finish or close what is open before opening the next. This keeps review
serial, keeps `main` close to every open branch, and prevents the
half-merged-stack problem entirely. With a solo maintainer this means the
repository never has more than one open PR.

## Workflow

1. Branch from up-to-date `main`. Branch names: `rNN/<topic>` for review
   round work (e.g. `r23/execution-plan`), `fix/<topic>` for hotfixes,
   `docs/<topic>` for documentation-only changes.
2. Commit as usual (no AI co-author trailer). Keep the branch focused on
   one deliverable; unrelated fixes get their own PR later.
3. Before opening the PR: `dotnet build Hermaeus.sln` clean (zero
   warnings), `dotnet test` green, docs and `CHANGELOG.md` truthful.
4. Open the PR against `main` using the template
   (`.github/PULL_REQUEST_TEMPLATE.md`). CI must pass.
5. Merge method: **squash merge** for fix/docs branches, **merge commit**
   for review-round branches (their commit sequence documents the round).
   Delete the branch after merge.
6. If a release follows, tag `main` after the merge per
   `docs/packaging.md`. Tags are never pushed from PR branches.

## Scope guidance

- A review round may land as one PR for the whole round or a small series
  of sequential PRs (one at a time, per the rule above); the round's
  roadmap doc decides.
- Urgent hotfixes on a broken `main` are the one case where direct push
  is still acceptable; note it in the commit message and open a follow-up
  issue if anything was skipped.

## Review expectations

Self-review is real review here while the maintainer count is one: read
the full diff on GitHub before merging, not in the editor you wrote it
in. The checklist in the PR template is the merge gate; an unchecked box
means the PR is not ready.
