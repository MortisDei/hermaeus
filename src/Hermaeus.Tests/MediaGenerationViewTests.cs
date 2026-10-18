using Avalonia.Controls;
using Avalonia.Threading;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Desktop.Views;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class MediaGenerationViewTests(AvaloniaTestHost avalonia)
{
    [Fact]
    public Task Only_the_current_media_view_decodes_a_new_preview() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = new MediaGenerationViewModel(new NeverGenerate(), NewSettings(temp));
        var obsolete = new MediaGenerationView { DataContext = vm };
        var firstWindow = Show(obsolete);
        await Drain();
        var active = new MediaGenerationView { DataContext = vm };
        var secondWindow = Show(active);
        try
        {
            await Drain();
            using var bitmap = new SkiaSharp.SKBitmap(2, 2);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            vm.SelectedOutput = new GeneratedMedia("generated.png", "image/png", data.ToArray());
            Assert.Null(obsolete.FindControl<Image>("PreviewImage")!.Source);
            Assert.NotNull(active.FindControl<Image>("PreviewImage")!.Source);
        }
        finally { firstWindow.Close(); secondWindow.Close(); await vm.ShutdownAsync(); }
    });

    [Fact]
    public Task Recreated_view_keeps_new_callbacks_and_same_context_reattaches() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = new MediaGenerationViewModel(new NeverGenerate(), NewSettings(temp));
        var first = new MediaGenerationView { DataContext = vm };
        var second = new MediaGenerationView { DataContext = vm };
        var firstWindow = Show(first);
        var secondWindow = Show(second);
        try
        {
            await Drain();
            var save = vm.RequestExportPath;
            var load = vm.RequestWorkflow;
            Assert.NotNull(save);
            Assert.NotNull(load);
            first.DataContext = null;
            await Drain();
            Assert.Same(save, vm.RequestExportPath);
            Assert.Same(load, vm.RequestWorkflow);
            secondWindow.Content = null;
            await Drain();
            Assert.Null(vm.RequestExportPath);
            Assert.Null(vm.RequestWorkflow);
            secondWindow.Content = second;
            await Drain();
            Assert.NotNull(vm.RequestExportPath);
            Assert.NotNull(vm.RequestWorkflow);
            Assert.NotSame(save, vm.RequestExportPath);
        }
        finally { firstWindow.Close(); secondWindow.Close(); await vm.ShutdownAsync(); }
    });

    [Fact]
    public Task Selected_image_gets_bounded_preview_and_audio_drops_the_bitmap() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = new MediaGenerationViewModel(new NeverGenerate(), NewSettings(temp));
        var view = new MediaGenerationView { DataContext = vm };
        var window = Show(view);
        try
        {
            await Drain();
            using var bitmap = new SkiaSharp.SKBitmap(2, 2);
            bitmap.Erase(SkiaSharp.SKColors.Red);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            vm.SelectedOutput = new GeneratedMedia("generated.png", "image/png", data.ToArray());
            Assert.NotNull(view.FindControl<Image>("PreviewImage")!.Source);
            vm.SelectedOutput = new GeneratedMedia("generated.wav", "audio/wav", "RIFF0000WAVE"u8.ToArray());
            Assert.Null(view.FindControl<Image>("PreviewImage")!.Source);
            Assert.Contains("Export", view.FindControl<TextBlock>("PreviewStatus")!.Text);
            vm.SelectedOutput = new GeneratedMedia("invalid.png", "image/png", [0, 1, 2]);
            Assert.Null(view.FindControl<Image>("PreviewImage")!.Source);
            Assert.NotEmpty(view.FindControl<TextBlock>("PreviewStatus")!.Text!);
            using var wide = new SkiaSharp.SKBitmap(16_385, 1);
            using var wideImage = SkiaSharp.SKImage.FromBitmap(wide);
            using var wideData = wideImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            vm.SelectedOutput = new GeneratedMedia("wide.png", "image/png", wideData.ToArray());
            Assert.Null(view.FindControl<Image>("PreviewImage")!.Source);
            Assert.Contains("dimension budget", view.FindControl<TextBlock>("PreviewStatus")!.Text);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
    });

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 800, Height = 600, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.Hide();
        return window;
    }
    private static async Task Drain() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    private sealed class NeverGenerate : IMediaGenerationService
    {
        public Task<MediaGenerationResult> GenerateAsync(MediaGenerationRequest request, IProgress<MediaGenerationProgress>? progress = null,
            CancellationToken ct = default) => throw new InvalidOperationException("This view test must not call a provider.");
    }
}
