using Aether.Desktop.Controls;
using Markdig;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// docs/review/02-onboarding-and-usability.md item 2.4 (only the pure,
/// no-UI-runtime-required pieces of MarkdownViewer are covered here; table
/// and link rendering themselves need a live Avalonia control tree and are
/// verified manually per the doc's acceptance criteria) and
/// docs/review/03-performance.md item 3.5 (incremental re-render).
/// </summary>
internal static class MarkdownViewerTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();


    public static Task LinkSchemeGateAllowsHttpAndHttps()
    {
        True(MarkdownViewer.IsSafeLinkScheme("https://example.com"), "https must be allowed.");
        True(MarkdownViewer.IsSafeLinkScheme("http://example.com"), "http must be allowed.");
        return Task.CompletedTask;
    }

    public static Task LinkSchemeGateRefusesDangerousOrMalformedSchemes()
    {
        False(MarkdownViewer.IsSafeLinkScheme("file:///etc/passwd"), "file: must be refused.");
        False(MarkdownViewer.IsSafeLinkScheme("javascript:alert(1)"), "javascript: must be refused.");
        False(MarkdownViewer.IsSafeLinkScheme("data:text/html,<script>alert(1)</script>"), "data: must be refused.");
        False(MarkdownViewer.IsSafeLinkScheme("not a url"), "An unparsable URL must be refused, not treated as safe by default.");
        False(MarkdownViewer.IsSafeLinkScheme(null), "A null URL must be refused.");
        False(MarkdownViewer.IsSafeLinkScheme(string.Empty), "An empty URL must be refused.");
        return Task.CompletedTask;
    }

    public static Task FenceLanguageNormalizationMapsKnownAliases()
    {
        Equal("C#", MarkdownViewer.NormalizeFenceLanguage("cs"), "cs should normalize to the C# highlighting name.");
        Equal("Bash", MarkdownViewer.NormalizeFenceLanguage("SH"), "Language matching should be case-insensitive.");
        Equal(null, MarkdownViewer.NormalizeFenceLanguage("not-a-real-language"), "Unrecognized languages should normalize to null (plain text).");
        Equal(null, MarkdownViewer.NormalizeFenceLanguage(null), "A missing fence language should normalize to null.");
        return Task.CompletedTask;
    }

    // ── 3.5 Incremental re-render ─────────────────────────────────────────────

    public static Task ReusePrefixMatchesOnlyLeadingIdenticalBlocks()
    {
        Equal(2, MarkdownViewer.ComputeReusePrefixLength(["a", "b", "c"], ["a", "b", "x"]), "The first two identical blocks should be reusable; the third differs.");
        Equal(0, MarkdownViewer.ComputeReusePrefixLength(["x", "b", "c"], ["a", "b", "c"]), "A mismatch at the very first block reuses nothing.");
        Equal(3, MarkdownViewer.ComputeReusePrefixLength(["a", "b", "c"], ["a", "b", "c"]), "Fully identical block lists reuse everything.");
        return Task.CompletedTask;
    }

    public static Task ReusePrefixNeverTreatsEmptySourceTextAsAMatch()
    {
        // An empty string is MarkdownViewer's sentinel for "span unavailable";
        // it must never be treated as matching, even against itself, since
        // that would risk reusing a stale control for genuinely new content.
        Equal(0, MarkdownViewer.ComputeReusePrefixLength(["", "b"], ["", "b"]), "An empty (invalid-span) entry must never count as a match.");
        return Task.CompletedTask;
    }

    public static Task ReusePrefixHandlesShrinkingOrGrowingBlockCounts()
    {
        Equal(2, MarkdownViewer.ComputeReusePrefixLength(["a", "b"], ["a", "b", "c"]), "Reuse is capped by the shorter (current) list when blocks were removed.");
        Equal(2, MarkdownViewer.ComputeReusePrefixLength(["a", "b", "c"], ["a", "b"]), "Reuse is capped by the shorter (previous) list when blocks were added.");
        return Task.CompletedTask;
    }

    private static readonly string[] GoldenDocuments =
    [
        "# Heading\n\nA plain paragraph with **bold** and *italic* text.",
        "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.",
        "- one\n- two\n- three",
        "> A blockquote spanning\n> two lines.",
        "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```",
        "| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |",
        "# Title\n\nIntro paragraph.\n\n- item one\n- item two\n\n```python\nprint('hi')\n```",
        "Check out [the docs](https://example.com/docs) for more.",
        "Para one.\n\n## Sub heading\n\nPara two with `inline code`.",
        "Line one\nLine two (soft break)\n\nNew paragraph after a blank line."
    ];

    public static Task IncrementalParsingConvergesToTheSameBlocksAsOneShotRendering()
    {
        foreach (var full in GoldenDocuments)
        {
            var oneShotDoc = Markdig.Markdown.Parse(full, Pipeline);
            var oneShotBlocks = MarkdownViewer.BlockSourceTexts(oneShotDoc, full);

            IReadOnlyList<string> incrementalBlocks = [];
            var len = Math.Min(40, full.Length);
            while (true)
            {
                var prefix = full[..len];
                var doc = Markdig.Markdown.Parse(prefix, Pipeline);
                incrementalBlocks = MarkdownViewer.BlockSourceTexts(doc, prefix);
                if (len >= full.Length) break;
                len = Math.Min(len + 40, full.Length);
            }

            Equal(oneShotBlocks.Count, incrementalBlocks.Count, $"Block count mismatch for document: {full}");
            for (var i = 0; i < oneShotBlocks.Count; i++)
                Equal(oneShotBlocks[i], incrementalBlocks[i], $"Block {i} text mismatch for document: {full}");
        }
        return Task.CompletedTask;
    }

    public static Task StreamingAppendOnlyDocumentReusesTheMajorityOfBlocks()
    {
        // Modeled on a real streamed chat reply: a couple of short paragraphs
        // that finish quickly, then one long paragraph appended token by
        // token. Once the first two blocks stabilize they should be reused
        // on every subsequent step while only the growing tail rebuilds.
        var full = "First paragraph.\n\nSecond paragraph.\n\n" + string.Concat(Enumerable.Repeat("more words appended one chunk at a time ", 20));
        IReadOnlyList<string> previousBlocks = [];
        var totalBlocks = 0;
        var totalReused = 0;

        for (var len = 10; len <= full.Length; len += 10)
        {
            var prefix = full[..Math.Min(len, full.Length)];
            var doc = Markdig.Markdown.Parse(prefix, Pipeline);
            var blocks = MarkdownViewer.BlockSourceTexts(doc, prefix);

            var reused = MarkdownViewer.ComputeReusePrefixLength(blocks, previousBlocks);
            totalBlocks += blocks.Count;
            totalReused += reused;
            previousBlocks = blocks;
        }

        True(totalReused > totalBlocks / 2, $"Expected the majority of blocks to be reused across an append-only stream; reused {totalReused} of {totalBlocks}.");
        return Task.CompletedTask;
    }
}
