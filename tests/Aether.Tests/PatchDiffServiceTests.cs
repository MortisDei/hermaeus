using System.Linq;
using Aether.Services;
using Xunit;

namespace Aether.Tests
{
    public class PatchDiffServiceTests
    {
        [Fact]
        public void ComputesAddedAndRemovedLines()
        {
            var svc = new PatchDiffService();
            var oldText = "a\nb\nc";
            var newText = "a\nc\nd";
            var diffs = svc.ComputeLineDiffs(oldText, newText).ToList();
            Assert.Contains(diffs, d => d.Type == DiffType.Unchanged && d.OldText == "a");
            Assert.Contains(diffs, d => d.Type == DiffType.Removed && d.OldText == "b");
            Assert.Contains(diffs, d => d.Type == DiffType.Added && d.NewText == "d");
        }

        [Fact]
        public void HandlesTrailingNewlinesConsistently()
        {
            var svc = new PatchDiffService();
            var oldText = "line1\nline2\n"; // trailing newline
            var newText = "line1\nline2"; // no trailing newline
            var diffs = svc.ComputeLineDiffs(oldText, newText).ToList();
            // Should treat lines as equal and not show spurious additions/removals
            Assert.DoesNotContain(diffs, d => d.Type == DiffType.Added || d.Type == DiffType.Removed);
        }

        // Large-file diffing can be fuzzy; prefer focused unit tests for behavior.
    }
}
