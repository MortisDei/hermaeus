<!-- One open PR per maintainer at any one time. If you already have a PR open, finish it first. See docs/pull-requests.md. -->

## What

<!-- One paragraph: what this PR changes and why. Link the review-round doc or issue if one exists. -->

## How verified

<!-- Build/test output summary; manual verification steps for UI changes (both themes if theme-sensitive). -->

## Checklist

- [ ] `dotnet build Hermaeus.sln` passes with zero warnings
- [ ] `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj` passes; new harness-style tests registered in `XunitHarnessTests.HarnessCases`
- [ ] Docs updated where behaviour changed (`docs/features.md`, workflow docs, `CHANGELOG.md`); nothing planned documented as existing
- [ ] No new NuGet packages, or written justification included below
- [ ] No em dashes introduced; no secrets, personal identifiers, or generated artifacts in the diff
- [ ] Security-relevant changes noted below and reflected in `docs/security-review.md`

## Security notes

<!-- "None" is a valid answer, but say it explicitly. -->
