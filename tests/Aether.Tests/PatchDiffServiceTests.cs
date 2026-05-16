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
    }
}
