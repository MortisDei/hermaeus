# Unresolved release gate

This native package's nuspec has no licence element, its nupkg contains no
standalone licence/NOTICE text, and its source commit
`d38ad748aee25600deb0e4880cd5b675b8742b23` has no repository URL. Looking up
that commit in SQLitePCL.raw returned 404. Do not infer its package terms
from the separately versioned Apache-licensed wrappers.

[SQLite's public-domain statement](https://sqlite.org/copyright.html) describes
the original SQLite code. That does not establish every provenance or
distribution term of this separately built native package. Its
[NuGet page](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/3.50.3)
contains the publisher's general SQLitePCLRaw README, which is not a substitute
for the missing source-specific declaration.

The existing security pin remains unchanged. Clarify the native package's
terms and provenance before release. This note is evidence of an unresolved
gate, not a licence or permission to redistribute.
