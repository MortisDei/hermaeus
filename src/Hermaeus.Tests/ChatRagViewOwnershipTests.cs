using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Eval;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Hermaeus.Desktop.Views;
using Hermaeus.Desktop.Converters;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class ChatRagViewOwnershipTests(AvaloniaTestHost avalonia)
{
    [Fact]
    public Task Queued_chat_dictation_cannot_retarget_a_replacement_context() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var first = NewChat(settings, new ControlledSpeech(temp.PathFor("dictation.wav")));
        var second = NewChat(NewSettings(temp, "second/settings.json"));
        var view = new ChatView { DataContext = first };
        var window = Show(view);
        try
        {
            await DrainAsync();
            await first.ChatMic.ToggleCommand.ExecuteAsync(null);
            await first.ChatMic.ToggleCommand.ExecuteAsync(null);
            // TranscriptReady queues the insertion. Replace its target before
            // the dispatcher can deliver that old presentation request.
            view.DataContext = second;
            await DrainAsync();
            Assert.Empty(first.InputText);
            Assert.Empty(second.InputText);
        }
        finally { window.Close(); first.ChatMic.Dispose(); second.ChatMic.Dispose(); }
    });

    [Fact]
    public Task Chat_dictation_delivers_once_to_the_active_view_and_survives_reload() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var speech = new ControlledSpeech(temp.PathFor("dictation.wav"));
        var vm = NewChat(settings, speech);
        var obsolete = new ChatView { DataContext = vm };
        var oldWindow = Show(obsolete);
        await DrainAsync();
        var active = new ChatView { DataContext = vm };
        var activeWindow = Show(active);
        try
        {
            await DrainAsync();
            oldWindow.Content = null;
            await DrainAsync();
            await DictateAsync(vm);
            Assert.Equal("dictated", vm.InputText);

            activeWindow.Content = null;
            await DrainAsync();
            vm.InputText = string.Empty;
            await DictateAsync(vm);
            Assert.Equal(string.Empty, vm.InputText);

            activeWindow.Content = active;
            await DrainAsync();
            await DictateAsync(vm);
            Assert.Equal("dictated", vm.InputText);
        }
        finally { oldWindow.Close(); activeWindow.Close(); vm.ChatMic.Dispose(); }
    });

    [Fact]
    public Task Obsolete_chat_context_cannot_clear_the_replacement_callbacks() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = NewChat(NewSettings(temp));
        var oldView = new ChatView { DataContext = vm };
        var oldWindow = Show(oldView);
        await DrainAsync();
        var replacement = new ChatView { DataContext = vm };
        var newWindow = Show(replacement);
        try
        {
            await DrainAsync();
            var copy = vm.RequestCopyToClipboard;
            var focus = vm.RequestInputFocus;
            var attach = vm.RequestContextFilePicker;
            var export = vm.RequestConversationExportPath;
            Assert.NotNull(copy);
            Assert.NotNull(focus);
            oldView.DataContext = null;
            await DrainAsync();
            Assert.Same(copy, vm.RequestCopyToClipboard);
            Assert.Same(focus, vm.RequestInputFocus);
            Assert.Same(attach, vm.RequestContextFilePicker);
            Assert.Same(export, vm.RequestConversationExportPath);
        }
        finally { oldWindow.Close(); newWindow.Close(); vm.ChatMic.Dispose(); }
    });

    [Fact]
    public Task Chat_unload_and_detached_context_change_wait_for_reload() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var first = NewChat(NewSettings(temp, "first/settings.json"));
        var second = NewChat(NewSettings(temp, "second/settings.json"));
        var view = new ChatView { DataContext = first };
        var window = Show(view);
        try
        {
            await DrainAsync();
            Assert.NotNull(first.RequestInputFocus);
            window.Content = null;
            Assert.False(view.IsAttachedToVisualTree());
            // Do not drain queued Unloaded first: detachment itself ends ownership.
            view.DataContext = second;
            Assert.Null(second.RequestInputFocus);
            await DrainAsync();
            Assert.Null(first.RequestInputFocus);
            Assert.Null(first.RequestCopyToClipboard);
            Assert.Null(first.RequestContextFilePicker);
            Assert.Null(first.RequestConversationExportPath);
            view.DataContext = second;
            Assert.Null(second.RequestInputFocus);
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(second.RequestInputFocus);
            Assert.NotNull(second.RequestCopyToClipboard);
            Assert.NotNull(second.RequestContextFilePicker);
            Assert.NotNull(second.RequestConversationExportPath);
            Assert.Null(first.RequestInputFocus);
        }
        finally { window.Close(); first.ChatMic.Dispose(); second.ChatMic.Dispose(); }
    });

    [Fact]
    public Task Obsolete_rag_context_cannot_clear_the_replacement_callbacks() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = await NewRagAsync(NewSettings(temp));
        var oldView = new RagView { DataContext = vm };
        var oldWindow = Show(oldView);
        await DrainAsync();
        var replacement = new RagView { DataContext = vm };
        var newWindow = Show(replacement);
        try
        {
            await DrainAsync();
            var copy = vm.RequestCopyToClipboard;
            var delete = vm.RequestDeleteDatasetConfirmation;
            var remove = vm.RequestRemoveMissingSourcesConfirmation;
            var refresh = vm.RequestConfirmWatchedRefresh;
            var folder = vm.RequestWatchedFolderPicker;
            Assert.NotNull(copy);
            Assert.NotNull(delete);
            oldView.DataContext = null;
            await DrainAsync();
            Assert.Same(copy, vm.RequestCopyToClipboard);
            Assert.Same(delete, vm.RequestDeleteDatasetConfirmation);
            Assert.Same(remove, vm.RequestRemoveMissingSourcesConfirmation);
            Assert.Same(refresh, vm.RequestConfirmWatchedRefresh);
            Assert.Same(folder, vm.RequestWatchedFolderPicker);
        }
        finally { oldWindow.Close(); newWindow.Close(); }
    });

    [Fact]
    public Task Rag_unload_and_detached_context_change_wait_for_reload() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var first = await NewRagAsync(NewSettings(temp, "first/settings.json"));
        var second = await NewRagAsync(NewSettings(temp, "second/settings.json"));
        var view = new RagView { DataContext = first };
        var window = Show(view);
        try
        {
            await DrainAsync();
            Assert.NotNull(first.RequestWatchedFolderPicker);
            window.Content = null;
            Assert.False(view.IsAttachedToVisualTree());
            view.DataContext = second;
            Assert.Null(second.RequestWatchedFolderPicker);
            await DrainAsync();
            Assert.Null(first.RequestWatchedFolderPicker);
            Assert.Null(first.RequestDeleteDatasetConfirmation);
            Assert.Null(first.RequestRemoveMissingSourcesConfirmation);
            Assert.Null(first.RequestConfirmWatchedRefresh);
            Assert.Null(first.RequestCopyToClipboard);
            view.DataContext = second;
            Assert.Null(second.RequestWatchedFolderPicker);
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(second.RequestWatchedFolderPicker);
            Assert.NotNull(second.RequestDeleteDatasetConfirmation);
            Assert.NotNull(second.RequestRemoveMissingSourcesConfirmation);
            Assert.NotNull(second.RequestConfirmWatchedRefresh);
            Assert.NotNull(second.RequestCopyToClipboard);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Rag_pending_delete_cannot_approve_after_the_owner_reloads() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = await NewRagAsync(NewSettings(temp));
        var view = new RagView { DataContext = vm };
        var window = Show(view);
        Window? dialog = null;
        try
        {
            window.Show();
            await DrainAsync();
            var item = new RagDatasetManagerItemViewModel(new RagDataset { Name = "Controlled dataset" }, "test");
            var pending = vm.RequestDeleteDatasetConfirmation!(item);
            Assert.False(pending.IsCompleted);
            dialog = Assert.Single(window.OwnedWindows);
            window.Content = null;
            await DrainAsync();
            window.Content = view;
            await DrainAsync();
            dialog.Close(true);
            Assert.False(await pending);
            dialog = null;
        }
        finally { dialog?.Close(); window.Close(); }
    });

    private static ChatViewModel NewChat(SettingsService settings, ControlledSpeech? speech = null)
    {
        // The plain native fixture omits App.axaml. Use the real converter
        // required by Chat's StaticResource without composing the owner app.
        Application.Current!.Resources["UppercaseConverter"] = UppercaseConverter.Instance;
        return new(
        new FakeLlm(), new ThrowingSaveConversationStore(), new FakeMemoryStore(), settings,
        new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
        new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService(),
        audioCapture: speech, sttProviders: speech);
    }

    private static async Task DictateAsync(ChatViewModel vm)
    {
        await vm.ChatMic.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(MicButtonState.Recording, vm.ChatMic.State);
        await vm.ChatMic.ToggleCommand.ExecuteAsync(null);
        await DrainAsync();
        Assert.Equal(MicButtonState.Ready, vm.ChatMic.State);
    }

    private sealed class ControlledSpeech(string wavPath) : IAudioCapture, ISpeechRecognitionProviderRegistry, ISpeechRecognitionService
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public string ProviderName => "Controlled speech";
        public IReadOnlyList<AudioInputDevice> EnumerateDevices() => [new("controlled", "Controlled")];
        public Task<ICaptureSession> StartAsync(string? deviceId = null, CancellationToken ct = default) =>
            Task.FromResult<ICaptureSession>(new Capture(wavPath));
        public SttProvider GetActiveProvider() => SttProvider.OnnxNative;
        public ISpeechRecognitionService GetActiveService() => this;
        public Task SetActiveProviderAsync(SttProvider provider) => Task.CompletedTask;
        public Task<SpeechTranscript> TranscribeAsync(Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default) =>
            Task.FromResult(new SpeechTranscript("dictated", 500, "en", false));

        private sealed class Capture(string path) : ICaptureSession
        {
            public event Action<float>? PeakLevelChanged { add { } remove { } }
            public string? Stop() { File.WriteAllBytes(path, [1, 2, 3]); return path; }
            public void Dispose() { }
        }
    }

    private static async Task<RagViewModel> NewRagAsync(SettingsService settings)
    {
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var embedding = new FakeEmbeddingService();
        var query = new RagQueryService(store, embedding, new FakeLlm(), settings, new NoOpReranker());
        return new RagViewModel(query, new RagPipeline(store, embedding),
            new RagEvalService(query, settings, new FakeEvalStore()), new FakeToasts(), new RuntimeLogService(settings), settings);
    }

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Content = content, Width = 800, Height = 600,
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        window.Hide();
        return window;
    }

    private static async Task DrainAsync() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
}
