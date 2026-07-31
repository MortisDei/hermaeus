using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// list_files shared the SEARCH result cap of 20, and the walk is a LIFO stack,
/// so a workspace with a few hundred files listed 20 entries from whichever
/// subtree happened to be popped first and said nothing about the rest. A real
/// run listed the workspace root, never saw a top-level folder that was
/// genuinely there, and told the user the directory did not exist.
/// </summary>
public sealed class AgentWorkspaceListingTests
{
    private static string BuildWorkspace(TempDir temp)
    {
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(root);

        // Mirrors the shape that broke: a fat subtree walked before a thin
        // sibling that the user cares about.
        var fat = Path.Combine(root, "chaos-generator", "Source");
        Directory.CreateDirectory(fat);
        for (var i = 0; i < 80; i++)
            File.WriteAllText(Path.Combine(fat, $"Generator{i}.cs"), "// generated");

        var thin = Path.Combine(root, "chaos-engine");
        Directory.CreateDirectory(Path.Combine(thin, "Source"));
        File.WriteAllText(Path.Combine(thin, "PurrfectChaos.Engine.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(thin, "Source", "Engine.cs"), "// engine");

        return root;
    }

    [Fact]
    public void A_top_level_folder_is_listed_however_deep_the_walk_goes_elsewhere()
    {
        using var temp = new TempDir();
        var root = BuildWorkspace(temp);

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root));

        Assert.Contains(listing, entry => entry.Contains("chaos-engine", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listing, entry => entry.Contains("PurrfectChaos.Engine.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Directories_are_listed_so_a_folder_is_never_invisible()
    {
        using var temp = new TempDir();
        var root = BuildWorkspace(temp);

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root));

        Assert.Contains(listing, entry => entry.TrimEnd('/').EndsWith("chaos-engine", StringComparison.OrdinalIgnoreCase)
            && entry.EndsWith('/'));
    }

    [Fact]
    public void An_empty_folder_still_appears_in_the_listing()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(Path.Combine(root, "no-source-here"));

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root));

        Assert.Contains(listing, entry => entry.StartsWith("no-source-here", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_listing_that_stops_early_says_so_instead_of_implying_the_rest_is_absent()
    {
        using var temp = new TempDir();
        var root = BuildWorkspace(temp);

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root) { MaxListResults = 10 });

        Assert.Equal(11, listing.Count);
        var notice = listing[^1];
        Assert.Contains("truncated", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_depth", notice, StringComparison.Ordinal);
        Assert.Contains("do not conclude a path is absent", notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_listing_is_stable_between_calls()
    {
        using var temp = new TempDir();
        var root = BuildWorkspace(temp);
        var tools = new AgentWorkspaceTools();

        var first = tools.ListFiles(new AgentWorkspaceOptions(root));
        var second = tools.ListFiles(new AgentWorkspaceOptions(root));

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_listing_budget_is_far_larger_than_the_search_budget()
    {
        var options = new AgentWorkspaceOptions("ignored");

        // They are different jobs: 20 search hits is a reasonable answer, 20
        // files is not a reasonable description of a workspace.
        Assert.True(options.MaxListResults > options.MaxSearchResults * 10,
            $"listing cap {options.MaxListResults} is too close to the search cap {options.MaxSearchResults}");
    }

    [Fact]
    public void Ignored_build_directories_stay_out_of_the_listing()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(Path.Combine(root, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        Directory.CreateDirectory(Path.Combine(root, "Source"));
        File.WriteAllText(Path.Combine(root, "bin", "Debug", "App.cs"), "// built");
        File.WriteAllText(Path.Combine(root, "Source", "App.cs"), "// source");

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root));

        Assert.DoesNotContain(listing, entry => entry.Contains("bin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listing, entry => entry.Contains("obj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listing, entry => entry.Contains("Source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Max_depth_still_bounds_the_listing()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
        File.WriteAllText(Path.Combine(root, "top.cs"), "// top");
        File.WriteAllText(Path.Combine(root, "a", "b", "c", "deep.cs"), "// deep");

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root), maxDepth: 1);

        Assert.Contains(listing, entry => entry.Contains("top.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listing, entry => entry.Contains("deep.cs", StringComparison.OrdinalIgnoreCase));
    }
}
