using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Hermaeus.Agent.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Desktop.Views;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class ViewOwnershipLifecycleTests(AvaloniaTestHost avalonia)
{
    [Fact]
    public Task Benchmark_failed_partial_load_is_visible_and_retries_after_reattachment() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        var attempts = 0;
        var failureShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var llm = new ScriptedModelsLlm(() => ++attempts == 1 ? throw new IOException("Controlled model-list failure") : []);
        var vm = new BenchmarkViewModel(new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore()),
            llm, new ModelProfileService(settings), settings, new FakeToasts());
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.HasLoaded) && vm.HasLoaded) loaded.TrySetResult();
            if (args.PropertyName != nameof(vm.Status)) return;
            if (vm.Status.Contains("Controlled model-list failure", StringComparison.Ordinal)) failureShown.TrySetResult();
        };
        var view = new BenchmarkView { DataContext = vm };
        var window = Show(view);
        try
        {
            await failureShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.HasLoaded);
            Assert.NotEmpty(vm.Suites); // The failed load was partial, not an empty VM.
            window.Content = null;
            await DrainAsync();
            window.Content = view;
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.HasLoaded);
            Assert.Equal(2, llm.GetModelsCallCount);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Benchmark_same_loaded_view_initialises_a_replacement_model() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var firstSettings = NewSettings(temp, "first/settings.json");
        var secondSettings = NewSettings(temp, "second/settings.json");
        firstSettings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets-a");
        secondSettings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets-b");
        var firstLlm = new ScriptedModelsLlm(() => []);
        var secondLlm = new ControlledModelsLlm();
        var first = new BenchmarkViewModel(new BenchmarkService(firstSettings, firstLlm, new FakeSystemInfo(), new FakeEvalStore()),
            firstLlm, new ModelProfileService(firstSettings), firstSettings, new FakeToasts());
        var second = new BenchmarkViewModel(new BenchmarkService(secondSettings, secondLlm, new FakeSystemInfo(), new FakeEvalStore()),
            secondLlm, new ModelProfileService(secondSettings), secondSettings, new FakeToasts());
        await first.LoadAsync();
        var view = new BenchmarkView { DataContext = first };
        var window = Show(view);
        try
        {
            await DrainAsync();
            view.DataContext = second;
            await secondLlm.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sharedLoad = second.LoadAsync();
            secondLlm.Continue.TrySetResult();
            await sharedLoad;
            Assert.Equal(1, secondLlm.GetModelsCallCount);
            Assert.Null(first.RequestClearRunHistoryConfirmation);
            Assert.NotNull(second.RequestClearRunHistoryConfirmation);
        }
        finally { secondLlm.Continue.TrySetResult(); window.Close(); }
    });

    private sealed class ControlledModelsLlm : ILlmService
    {
        public string ProviderName => "Controlled fixture";
        public bool IsConfigured => true;
        public int GetModelsCallCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
        {
            GetModelsCallCount++;
            Started.TrySetResult();
            await Continue.Task.WaitAsync(ct);
            return [];
        }
        public IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(string modelId, IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null, CancellationToken ct = default) =>
            new FakeLlm().StreamChatAsync(modelId, messages, options, ct);
    }

    [Theory]
    [InlineData("Activity")]
    [InlineData("Memories")]
    [InlineData("Logs")]
    [InlineData("Doctor")]
    [InlineData("Wizard")]
    [InlineData("Agent")]
    [InlineData("Models")]
    public Task Sibling_views_release_callbacks_and_preserve_a_newer_owner(string surface) => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        using var tts = NewTtsSettingsViewModel(settings);
        var services = NewServicesViewModel(settings, tts);
        var llm = new FakeLlm();
        var logs = new RuntimeLogService(settings);
        var tools = new AgentWorkspaceTools();
        var store = new FileAgentTaskStateStore(settings);
        var memory = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var manifests = new WorkspaceManifestService();
        var query = new RagQueryService(new SqliteRagStore(settings), new FakeEmbeddingService(), llm, settings, new NoOpReranker());
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
        (object Model, Func<Control> View, string[] Callbacks) subject = surface switch
        {
            "Activity" => (new ActivityViewModel(new FakeToasts()), () => new ActivityView(), ["RequestConfirmClear"]),
            "Memories" => (new MemoriesViewModel(new MemoryStore(settings), new ConversationStore(settings), settings, new FakeToasts()),
                () => new MemoriesView(), ["RequestDeleteConfirmation"]),
            "Logs" => (new LogsViewModel(logs, new RedactionService()), () => new LogsView(), ["RequestCopyToClipboard"]),
            "Doctor" => (new DoctorViewModel(new FakeDoctorService(), new FakeToasts(), settings), () => new DoctorView(),
                ["RequestCopyToClipboard", "RequestConfirmAsync"]),
            "Wizard" => (new SetupWizardViewModel(settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
                new FakeDoctorService(), new FakeToasts(), new FakeSystemInfo()), () => new SetupWizardView(),
                ["RequestDataRootPicker", "RequestLocalAiAssetsRootPicker", "RequestModelFolderPicker"]),
            "Agent" => (new AgentViewModel(agentService, store, memory, tools, llm, query, logs,
                new WorkspaceAnalysisService(profiles, memory), new WorkspaceActivationService(manifests, profiles), manifests, settings),
                () => new AgentView(), ["RequestWorkspaceRootPicker", "RequestDeleteTaskConfirmation"]),
            "Models" => (new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings,
                new FakeSystemInfo(), services, new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService()),
                () => new ModelManagementView(), ["RequestOrganizeConfirmation", "RequestRepoIdInput", "RequestDeleteModelConfirmation",
                    "RequestEmptyDirectoryCleanupConfirmation", "RequestCompanionDisableConfirmation", "RequestCompanionRemovalConfirmation"]),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        object?[] Callbacks() => subject.Callbacks.Select(name => subject.Model.GetType().GetProperty(name)!.GetValue(subject.Model)).ToArray();
        var first = subject.View();
        first.DataContext = subject.Model;
        Assert.All(Callbacks(), Assert.Null);
        var firstWindow = Show(first);
        Window? replacementWindow = null;
        try
        {
            await DrainAsync();
            Assert.All(Callbacks(), callback => Assert.IsAssignableFrom<Delegate>(callback));
            var replacement = subject.View();
            replacement.DataContext = subject.Model;
            replacementWindow = Show(replacement);
            await DrainAsync();
            var current = Callbacks();
            first.DataContext = null;
            await DrainAsync();
            Assert.Equal(current, Callbacks());
            replacementWindow.Content = null;
            await DrainAsync();
            Assert.All(Callbacks(), Assert.Null);
            replacementWindow.Content = replacement;
            await DrainAsync();
            Assert.All(Callbacks(), callback => Assert.IsAssignableFrom<Delegate>(callback));
        }
        finally { firstWindow.Close(); replacementWindow?.Close(); DisposeServers(services); }
    });

    [Fact]
    public Task Activity_template_recreation_does_not_accumulate_obsolete_views() => avalonia.RunAsync(async () =>
    {
        var model = new ActivityViewModel(new FakeToasts());
        var host = new ContentControl();
        host.DataTemplates.Add(new FuncDataTemplate<ActivityViewModel>((_, _) => new ActivityView()));
        var window = Show(host);
        var previous = new List<WeakReference>();
        try
        {
            for (var cycle = 0; cycle < 20; cycle++) previous.Add(await CycleActivityAsync(host, model));
            // A ContentPresenter may retain a recyclable last child while
            // its host survives. Release the test's host/template ownership
            // before attributing any remaining root to the feature model.
            window.Content = null;
            window.Close();
            host.DataTemplates.Clear();
            host = null!;
            window = null!;
            await DrainAsync();
            for (var attempt = 0; attempt < 10 && previous.Any(weak => weak.IsAlive); attempt++)
            {
                await DrainAsync();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            Assert.All(previous, weak => Assert.False(weak.IsAlive));
            GC.KeepAlive(model);
        }
        finally { window?.Close(); }
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CycleActivityAsync(ContentControl host, ActivityViewModel model)
    {
        host.Content = model;
        await DrainAsync();
        var view = Assert.Single(host.GetVisualDescendants().OfType<ActivityView>());
        var weak = new WeakReference(view);
        var callback = Assert.IsType<Func<Task<bool>>>(model.RequestConfirmClear);
        Assert.True(weak.IsAlive);
        host.Content = null;
        await DrainAsync();
        Assert.Null(model.RequestConfirmClear);
        Assert.False(await callback());
        view = null!;
        callback = null!;
        return weak;
    }

    [Fact]
    public Task Services_same_context_reattachment_wires_servers_added_while_detached() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var tts = NewTtsSettingsViewModel(settings);
        var vm = NewServicesViewModel(settings, tts);
        var view = new ServicesView { DataContext = vm };
        var window = Show(view);
        using var added = NewServer(settings);
        try
        {
            await DrainAsync();
            window.Content = null;
            await DrainAsync();
            vm.Servers.Add(added);
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(added.RequestFilePicker);

            var current = added.RequestFilePicker;
            await DrainAsync();
            Assert.Same(current, added.RequestFilePicker);
        }
        finally { window.Close(); DisposeServers(vm); }
    });

    [Fact]
    public Task Services_removal_and_context_clear_release_owned_child_callbacks() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var tts = NewTtsSettingsViewModel(settings);
        var vm = NewServicesViewModel(settings, tts);
        var view = new ServicesView { DataContext = vm };
        var window = Show(view);
        var removed = vm.Servers[0];
        try
        {
            await DrainAsync();
            Assert.NotNull(removed.RequestFilePicker);
            vm.Servers.Remove(removed);
            Assert.Null(removed.RequestFilePicker);
            view.DataContext = null;
            Assert.All(vm.Servers, server => Assert.Null(server.RequestFilePicker));
            Assert.Null(tts.RequestTtsScriptPicker);
            Assert.Null(tts.RequestTtsVoiceSamplePicker);
        }
        finally { window.Close(); removed.Dispose(); DisposeServers(vm); }
    });

    [Fact]
    public Task Obsolete_services_owner_does_not_clear_replacement_callbacks() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var tts = NewTtsSettingsViewModel(settings);
        var vm = NewServicesViewModel(settings, tts);
        var oldView = new ServicesView { DataContext = vm };
        var oldWindow = Show(oldView);
        await DrainAsync();
        var replacement = new ServicesView { DataContext = vm };
        var replacementWindow = Show(replacement);
        await DrainAsync();
        var serverPicker = vm.Servers[0].RequestFilePicker;
        var voicePicker = tts.RequestTtsScriptPicker;
        try
        {
            Assert.NotNull(serverPicker);
            Assert.NotNull(voicePicker);
            oldView.DataContext = null;
            await DrainAsync();
            Assert.Same(serverPicker, vm.Servers[0].RequestFilePicker);
            Assert.Same(voicePicker, tts.RequestTtsScriptPicker);
            replacement.DataContext = null;
            Assert.Null(vm.Servers[0].RequestFilePicker);
            Assert.Null(tts.RequestTtsScriptPicker);
        }
        finally { oldWindow.Close(); replacementWindow.Close(); DisposeServers(vm); }
    });

    [Fact]
    public Task Settings_replaced_context_cannot_deliver_appearance_preview() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var first = NewSettingsViewModel(NewSettings(temp, "first/settings.json"), new FakeSecretStore());
        var second = NewSettingsViewModel(NewSettings(temp, "second/settings.json"), new FakeSecretStore());
        var view = new SettingsView { DataContext = first };
        var window = Show(view);
        var resources = Application.Current!.Resources;
        var previous = resources.TryGetValue("AppChatFontSize", out var original) ? original : null;
        try
        {
            await DrainAsync();
            view.DataContext = second;
            second.Ui.FontSize = 19;
            Assert.Equal(19d, resources["AppChatFontSize"]);
            first.Ui.FontSize = 21;
            Assert.Equal(19d, resources["AppChatFontSize"]);
            view.DataContext = null;
            second.Ui.FontSize = 23;
            Assert.Equal(19d, resources["AppChatFontSize"]);
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            await first.ShutdownAsync();
            await second.ShutdownAsync();
            if (previous is null) resources.Remove("AppChatFontSize");
            else resources["AppChatFontSize"] = previous;
        }
    });

    [Fact]
    public Task Settings_and_nested_memory_clear_only_their_own_callbacks() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = NewSettingsViewModel(NewSettings(temp), new FakeSecretStore());
        var first = new SettingsView { DataContext = vm };
        var firstWindow = Show(first);
        await DrainAsync();
        var replacement = new SettingsView { DataContext = vm };
        var replacementWindow = Show(replacement);
        await DrainAsync();
        var picker = vm.Data.RequestDataRootPicker;
        var memory = vm.Memory.RequestConfirmClearIndex;
        try
        {
            Assert.NotNull(picker);
            Assert.NotNull(memory);
            first.DataContext = null;
            await DrainAsync();
            Assert.Same(picker, vm.Data.RequestDataRootPicker);
            Assert.Same(memory, vm.Memory.RequestConfirmClearIndex);
            replacement.DataContext = null;
            await DrainAsync();
            Assert.Null(vm.Data.RequestDataRootPicker);
            Assert.Null(vm.Ui.RequestPetManifestPicker);
            Assert.Null(vm.LocalAiSetup.RequestCopyToClipboard);
            Assert.Null(vm.Memory.RequestConfirmClearIndex);
        }
        finally { firstWindow.Close(); replacementWindow.Close(); await vm.ShutdownAsync(); }
    });

    [Fact]
    public Task Settings_reload_reconnects_callbacks_and_detached_replacement_waits_for_load() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var first = NewSettingsViewModel(NewSettings(temp, "first/settings.json"), new FakeSecretStore());
        var second = NewSettingsViewModel(NewSettings(temp, "second/settings.json"), new FakeSecretStore());
        var view = new SettingsView { DataContext = first };
        Assert.Null(first.Data.RequestDataRootPicker);
        var window = Show(view);
        try
        {
            await DrainAsync();
            Assert.NotNull(first.Data.RequestDataRootPicker);
            Assert.NotNull(first.Memory.RequestConfirmClearIndex);
            window.Content = null;
            await DrainAsync();
            Assert.Null(first.Data.RequestDataRootPicker);
            Assert.Null(first.Memory.RequestConfirmClearIndex);
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(first.Data.RequestDataRootPicker);
            Assert.NotNull(first.Memory.RequestConfirmClearIndex);

            window.Content = null;
            await DrainAsync();
            view.DataContext = second;
            await DrainAsync();
            Assert.Null(second.Data.RequestDataRootPicker);
            Assert.Null(second.Memory.RequestConfirmClearIndex);
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(second.Data.RequestDataRootPicker);
            Assert.NotNull(second.Memory.RequestConfirmClearIndex);
            Assert.Null(first.Data.RequestDataRootPicker);
        }
        finally { window.Close(); await first.ShutdownAsync(); await second.ShutdownAsync(); }
    });

    [Fact]
    public Task Services_detached_context_replacement_waits_for_load() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var tts = NewTtsSettingsViewModel(settings);
        var first = NewServicesViewModel(settings, tts);
        var second = NewServicesViewModel(settings, tts);
        var view = new ServicesView { DataContext = first };
        Assert.All(first.Servers, server => Assert.Null(server.RequestFilePicker));
        var window = Show(view);
        try
        {
            await DrainAsync();
            window.Content = null;
            await DrainAsync();
            view.DataContext = second;
            Assert.All(second.Servers, server => Assert.Null(server.RequestFilePicker));
            Assert.Null(tts.RequestTtsScriptPicker);
            window.Content = view;
            await DrainAsync();
            Assert.All(first.Servers, server => Assert.Null(server.RequestFilePicker));
            Assert.All(second.Servers, server => Assert.NotNull(server.RequestFilePicker));
            Assert.NotNull(tts.RequestTtsScriptPicker);
        }
        finally { window.Close(); DisposeServers(first); DisposeServers(second); }
    });

    [Fact]
    public Task Detached_memory_confirmation_cannot_approve_after_its_owner_is_rebound() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var vm = NewSettingsViewModel(NewSettings(temp), new FakeSecretStore());
        var view = new SettingsMemorySectionView { DataContext = vm.Memory };
        var window = Show(view);
        Window? dialog = null;
        try
        {
            window.Show(); // The real modal owner must be visible; opacity remains zero.
            await DrainAsync();
            var pending = Assert.IsType<Func<Task<bool>>>(vm.Memory.RequestConfirmClearIndex)();
            Assert.False(pending.IsCompleted);
            dialog = Assert.Single(window.OwnedWindows);
            window.Content = null;
            await DrainAsync();
            window.Content = view;
            await DrainAsync();
            Assert.NotNull(vm.Memory.RequestConfirmClearIndex);
            dialog.Close(true);
            Assert.False(await pending);
            dialog = null;
        }
        finally { dialog?.Close(); window.Close(); await vm.ShutdownAsync(); }
    });

    [Fact]
    public Task Detached_services_view_is_collectible_while_feature_model_survives() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var tts = NewTtsSettingsViewModel(settings);
        var vm = NewServicesViewModel(settings, tts);
        var weak = await CreateDetachedServicesViewAsync(vm);
        try
        {
            for (var attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
            {
                await DrainAsync();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(weak.IsAlive, "The detached view remained reachable while its feature model survived.");
            GC.KeepAlive(vm);
        }
        finally { DisposeServers(vm); }
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CreateDetachedServicesViewAsync(ServicesViewModel vm)
    {
        var view = new ServicesView { DataContext = vm };
        var weak = new WeakReference(view);
        var window = Show(view);
        await DrainAsync();
        Assert.True(weak.IsAlive); // Positive control while the native host owns the view.
        window.Content = null;
        await DrainAsync();
        window.Close();
        view = null!;
        window = null!;
        return weak;
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

    private static ServerProcessViewModel NewServer(Hermaeus.Core.Services.ISettingsService settings) =>
        new(new ServerConfig { Name = "Added server" }, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings));

    private static void DisposeServers(ServicesViewModel vm)
    {
        foreach (var server in vm.Servers) server.Dispose();
    }
}
