using System.Text.Json;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Every NuGet package Hermaeus redistributes must be named in
/// THIRD-PARTY-NOTICES.md.
///
/// A notices file is exactly the kind of document that rots: it is written
/// once, nobody reads it again, and six package additions later it describes a
/// dependency set that no longer exists. Adding a package now fails the build
/// until the notice is written in the same change.
///
/// Deliberately dumb, like the other guards here: read the resolved dependency
/// closure out of the restore assets and check each package name appears in the
/// file. It does not attempt to validate licence text, which is a job for a
/// human and a lawyer, not a regex.
/// </summary>
public sealed class ThirdPartyNoticeGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>The projects that actually ship in a release.</summary>
    private static readonly string[] ShippedProjects = ["Hermaeus.Desktop", "Hermaeus.LocalApi"];

    private static IReadOnlyList<(string Name, string Version)> ShippedPackages()
    {
        var packages = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in ShippedProjects)
        {
            var assets = Path.Combine(RepoRoot, "src", project, "obj", "project.assets.json");
            if (!File.Exists(assets))
                continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(assets));
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                continue;

            foreach (var library in libraries.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package")
                    continue;

                var parts = library.Name.Split('/');
                if (parts.Length == 2)
                    packages[parts[0]] = parts[1];
            }
        }

        return [.. packages.Select(p => (p.Key, p.Value))];
    }

    [Fact]
    public void Every_redistributed_package_is_named_in_the_third_party_notices()
    {
        var packages = ShippedPackages();

        // If restore has not run for the shipped projects there is nothing to
        // check, and failing here would be a false alarm about the notices.
        Assert.True(packages.Count > 0,
            "could not read the shipped dependency closure; run a restore/build before this test");

        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));
        var missing = packages
            .Where(p => !notices.Contains(p.Name, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Name} {p.Version}")
            .ToList();

        Assert.True(missing.Count == 0,
            "These packages ship inside Hermaeus and are not named in THIRD-PARTY-NOTICES.md. "
            + "Add them to section 1 with their licence and copyright, taken from the package's own .nuspec: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void The_notices_state_the_licence_of_the_things_hermaeus_downloads_but_does_not_ship()
    {
        // These are not in any dependency closure, so nothing else would catch
        // their removal. They are the entries most likely to matter, including
        // the CUDA runtime because it is not open source.
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));

        foreach (var subject in new[]
                 {
                     "llama.cpp",
                     "NVIDIA",          // the cudart companion archive is not open source
                     "Hugging Face",    // terms of service and trademark
                     "Qwen3",
                     "Gemma 4",
                     "Kokoro",
                     "whisper",
                 })
        {
            Assert.True(notices.Contains(subject, StringComparison.OrdinalIgnoreCase),
                $"THIRD-PARTY-NOTICES.md no longer mentions '{subject}'. Hermaeus downloads or calls it, "
                + "so its terms belong in the notices even though it is not redistributed.");
        }
    }

    [Fact]
    public void Every_starter_model_licence_appears_in_the_notices()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));

        foreach (var entry in Hermaeus.Services.StarterModelCatalog.All)
        {
            Assert.True(notices.Contains(entry.License, StringComparison.OrdinalIgnoreCase),
                $"the starter model '{entry.Id}' is offered under '{entry.License}', which is not named in "
                + "THIRD-PARTY-NOTICES.md section 5.");
        }
    }
}
