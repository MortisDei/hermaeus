using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hermaeus.Desktop.Controls;
using Markdig;
using Markdig.Syntax;
using Xunit;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class MarkdownViewerLifecycleTests(AvaloniaTestHost avalonia)
{
    [Fact]
    public Task Tab_switches_resume_rendering_including_updates_while_detached() => avalonia.RunAsync(async () =>
    {
        using var viewer = new MarkdownViewer { Markdown = "Initial response" };
        var (window, tabs) = CreateTabs(viewer);
        try
        {
            await WaitForAsync(() => RenderedText(viewer) == "Initial response");
            for (var round = 1; round <= 2; round++)
            {
                Select(tabs, 1);
                Assert.False(viewer.IsAttachedToVisualTree());
                var previousContent = viewer.Content;
                viewer.Markdown = $"Hidden response {round}";
                Assert.Same(previousContent, viewer.Content);

                Select(tabs, 0);
                Assert.True(viewer.IsAttachedToVisualTree());
                await WaitForAsync(() => RenderedText(viewer) == $"Hidden response {round}");

                viewer.Markdown = $"Visible response {round}";
                await WaitForAsync(() => RenderedText(viewer) == $"Visible response {round}");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Reattachment_retries_an_abandoned_initial_parse() => avalonia.RunAsync(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldParse = new TaskCompletionSource<MarkdownDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var viewer = new MarkdownViewer(markdown =>
        {
            if (++calls == 1)
            {
                started.TrySetResult();
                return heldParse.Task;
            }
            return Task.FromResult(Markdig.Markdown.Parse(markdown));
        }) { Markdown = "Response before switching" };
        var (window, tabs) = CreateTabs(viewer);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Null(viewer.Content);
            Select(tabs, 1);
            Assert.False(viewer.IsAttachedToVisualTree());
            Select(tabs, 0);
            Assert.True(viewer.IsAttachedToVisualTree());

            await WaitForAsync(() => RenderedText(viewer) == "Response before switching");
            Assert.Equal(2, calls);
            var recoveredContent = viewer.Content;
            heldParse.SetResult(Markdig.Markdown.Parse("Abandoned parse must not replace the response"));
            await Task.Delay(150);
            Assert.Same(recoveredContent, viewer.Content);
            Assert.Equal("Response before switching", RenderedText(viewer));
        }
        finally
        {
            heldParse.TrySetResult(Markdig.Markdown.Parse("Response before switching"));
            window.Close();
        }
    });

    [Fact]
    public Task Disposed_viewer_ignores_a_pending_parse_and_later_reattachment() => avalonia.RunAsync(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldParse = new TaskCompletionSource<MarkdownDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var viewer = new MarkdownViewer(_ =>
        {
            calls++;
            started.TrySetResult();
            return heldParse.Task;
        }) { Markdown = "Pending response" };
        var (window, tabs) = CreateTabs(viewer);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            viewer.Dispose();
            heldParse.SetResult(Markdig.Markdown.Parse("Discarded response"));
            Select(tabs, 1);
            Select(tabs, 0);
            viewer.Markdown = "Later response";
            await Task.Delay(200); // Allow the pending continuation and two render intervals.
            Assert.Null(viewer.Content);
            Assert.Equal(1, calls);
        }
        finally
        {
            heldParse.TrySetResult(Markdig.Markdown.Parse("Pending response"));
            window.Close();
        }
    });

    private static (Window Window, TabControl Tabs) CreateTabs(MarkdownViewer viewer)
    {
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Response", Content = viewer },
                new TabItem { Header = "Other", Content = new TextBlock { Text = "Other tab" } }
            },
            SelectedIndex = 0
        };
        // Showing creates the native presentation source. Keep this test window
        // invisible, inactive and off the taskbar, then hide it immediately.
        // This exercises control attachment, not pixel or owner GUI proof.
        var window = new Window
        {
            Content = tabs, Width = 640, Height = 480,
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        window.Hide();
        Select(tabs, 0);
        Assert.True(viewer.IsAttachedToVisualTree());
        return (window, tabs);
    }

    private static void Select(TabControl tabs, int index)
    {
        tabs.SelectedIndex = index;
        tabs.ApplyTemplate();
        tabs.Measure(new Size(640, 480));
        tabs.Arrange(new Rect(0, 0, 640, 480));
        tabs.UpdateLayout();
    }

    private static string RenderedText(MarkdownViewer viewer) => viewer.Content is Control content
        ? string.Concat(content.GetSelfAndVisualDescendants().OfType<SelectableTextBlock>()
            .Select(block => block.Inlines?.Text ?? block.Text))
        : string.Empty;

    private static async Task WaitForAsync(Func<bool> ready)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready() && elapsed.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(10);
        Assert.True(ready(), "The real control did not reach the expected render state.");
    }
}
