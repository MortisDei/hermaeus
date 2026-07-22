using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r20 rename: "Aether" carried real trademark risk (see docs/review/archived/r20),
/// so the product became Hermaeus. A stray "Aether" left behind by a future
/// edit or a bad merge should fail the build instead of shipping quietly.
/// Reuses the file-walking approach ServiceTests.SourceStringsAvoidLongDashes
/// uses for the em-dash guard.
/// </summary>
public sealed class NamingConsistencyTests
{
    /// <summary>
    /// Deliberate, reviewed exceptions: (relative file path, allowed substrings).
    /// Every literal "aether" in a scanned file must be covered by one of its
    /// file's entries here, or the file is not covered at all and any
    /// occurrence fails. Register new legacy references here consciously
    /// instead of loosening the scan.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowlistedContent = new(StringComparer.OrdinalIgnoreCase)
    {
        // 2.2 shim: renames the pre-rename schema-version bookkeeping table in
        // place instead of re-running every migration against a live database.
        ["src/Hermaeus.Rag/Storage/SqliteMigrationRunner.cs"] = ["aether_schema_versions"],
        ["src/Hermaeus.Agent/Services/SqliteMigrationRunner.cs"] = ["aether_schema_versions"],
        ["src/Hermaeus.Services/SqliteMigrationRunner.cs"] = ["aether_schema_versions"],
        ["src/Hermaeus.Tests/MigrationRunnerTests.cs"] = ["aether_schema_versions"],
        ["src/Hermaeus.Tests/LessonStoreTests.cs"] = ["aether_schema_versions"],

        // 2.8 shim: reads a pre-rename workspace manifest as a fallback; always
        // writes to the new path.
        ["src/Hermaeus.Agent/Services/WorkspaceManifestService.cs"] = [".aether"],
        ["src/Hermaeus.Tests/WorkspaceManifestLegacyFallbackTests.cs"] = [".aether", "aether_manifest_path"],

        // 2.9: "aether" is kept as a real English word CMUdict may still miss,
        // alongside the new "hermaeus" product-name pronunciation.
        ["src/Hermaeus.Voice/KokoroUserLexicon.cs"] = ["aether"],

        // This file's own allowlist/scan logic necessarily mentions "aether".
        ["src/Hermaeus.Tests/NamingConsistencyTests.cs"] = ["aether"],

        // r20 security-review subsection references the old name and the two
        // legacy-read shims by their exact pre-rename identifiers.
        ["docs/security-review.md"] =
        [
            "aether_schema_versions",
            ".aether/workspace.json",
            "old `Aether` service name",
            "Rename Aether to Hermaeus",
        ],
    };

    private static readonly HashSet<string> AllowlistedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHANGELOG.md",
        "docs/changelog-archive.md",
    };

    [Fact]
    public void No_stray_aether_references_remain()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var offenders = new List<string>();

        var scanRoots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "scripts"),
            Path.Combine(root, ".github"),
            Path.Combine(root, ".claude", "skills"),
            Path.Combine(root, "docs"),
        };

        var candidates = scanRoots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly))
            .Concat(new[]
            {
                Path.Combine(root, "Hermaeus.sln"),
                Path.Combine(root, "Directory.Build.props"),
                Path.Combine(root, "build.sh"),
                Path.Combine(root, "build.ps1"),
            })
            .Where(File.Exists)
            .Where(path => !path.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .Distinct();

        foreach (var path in candidates)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.StartsWith("docs/review/", StringComparison.OrdinalIgnoreCase))
                continue;
            // docs/temp/ is gitignored, owner-personal scratch space (go-public
            // checklist drafts); not part of the shipped, reviewed repo.
            if (relative.StartsWith("docs/temp/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (AllowlistedPaths.Contains(relative))
                continue;

            var content = File.ReadAllText(path);
            if (AllowlistedContent.TryGetValue(relative, out var allowed))
                foreach (var substring in allowed)
                    content = content.Replace(substring, "", StringComparison.OrdinalIgnoreCase);

            if (content.Contains("aether", StringComparison.OrdinalIgnoreCase))
                offenders.Add(relative);
        }

        Assert.True(offenders.Count == 0,
            "Files still contain 'Aether' after the r20 rename to Hermaeus: " + string.Join(", ", offenders));
    }
}
