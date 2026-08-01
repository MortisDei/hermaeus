using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 01 1.3: the last assistant message's copy/read-aloud row sits
/// outside its message border and ended up flush against the input bar, where
/// it could not reliably be clicked. The fix is a spacer inside the scrolled
/// content, because ScrollViewer.Padding was not producing usable extent.
///
/// This pins the presence of the fix, not the pixel result. A layout assertion
/// is not available without a live visual tree, and the pixel result needs a
/// human looking at the running app.
/// </summary>
public sealed class ChatTranscriptLayoutGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Theory]
    // Every scrolling panel with a docked input bar underneath it has the same
    // defect and needs the same spacer. The owner found the RAG panel after the
    // chat one was fixed.
    [InlineData("ChatView.axaml", "TranscriptTrailingSpacer")]
    [InlineData("RagView.axaml", "RagTrailingSpacer")]
    public void A_panel_with_a_docked_input_bar_keeps_a_trailing_spacer(string view, string spacerName)
    {
        var path = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", view);
        var xaml = File.ReadAllText(path);

        Assert.Contains(spacerName, xaml);
    }
}
