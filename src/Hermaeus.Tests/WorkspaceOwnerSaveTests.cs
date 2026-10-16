using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class WorkspaceOwnerSaveTests
{
    [Fact]
    public async Task Editor_save_waits_for_the_agent_target_owner_and_refuses_its_stale_preimage()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");
        var tools = new AgentWorkspaceTools();
        var owner = new ControlledTargetOwner();
        var memory = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memory.InitializeAsync();
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var llm = new FakeLlm();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), llm, settings, new NoOpReranker());
        var manifests = new WorkspaceManifestService();
        var profiles = new FileWorkspaceProfileStore(settings);
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(),
            new AgentToolExecutor(tools), new FakeAgentLlm(), workspaceTools: tools, commandOwner: owner);
        var view = new AgentViewModel(agent, store, memory, tools, llm, rag,
            new RuntimeLogService(settings), new WorkspaceAnalysisService(profiles, memory),
            new WorkspaceActivationService(manifests, profiles), manifests, settings, commandOwner: owner);
        view.WorkspaceRoot = workspace;

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (view.WorkspaceFilePreview == "before") loaded.TrySetResult();
        }
        view.PropertyChanged += OnChanged;
        try
        {
            view.SelectedWorkspaceFile = new AgentWorkspaceFileViewModel("notes.md", "", null);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { view.PropertyChanged -= OnChanged; }
        view.DraftProposedContent = "owner edit";
        Assert.True(view.CanSaveWorkspaceFile);

        var task = new AgentTaskState { Goal = "Agent edit", WorkspaceRoot = workspace, Status = AgentTaskStatus.Complete };
        await store.SaveAsync(task);
        var review = new AgentPatchReviewService(tools, store, commandOwner: owner);
        var options = new AgentWorkspaceOptions(workspace);
        var patch = await review.QueueAsync(task.TaskId, "notes.md", "Agent edit", "agent edit", options);
        task = (await store.LoadAsync(task.TaskId))!;
        var apply = review.ApplyAsync(task, patch, options);
        Task? save = null;
        try
        {
            await owner.FirstTargetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            save = view.SaveWorkspaceFileCommand.ExecuteAsync(null);
            // An explicit second gate-entry signal races the actual editor
            // command. No sleep is used to guess whether a save is queued.
            var first = await Task.WhenAny(owner.SecondTargetRequested.Task, save).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(owner.SecondTargetRequested.Task, first);
            Assert.False(save.IsCompleted);
            Assert.Equal("before", await File.ReadAllTextAsync(path));

            owner.ReleaseFirstTarget.TrySetResult();
            Assert.Equal(AgentMutationOutcome.Applied, await apply.WaitAsync(TimeSpan.FromSeconds(5)));
            await save.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("agent edit", await File.ReadAllTextAsync(path));
            Assert.Equal("Conflict. Reload before saving.", view.WorkspaceEditorStatus);
            Assert.True(view.WorkspaceEditorDirty);
            Assert.True(view.IsError);
        }
        finally
        {
            owner.ReleaseFirstTarget.TrySetResult();
            await apply.WaitAsync(TimeSpan.FromSeconds(5));
            if (save is not null) await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_editor_save_uses_its_captured_content_and_preserves_newer_editor_state(bool switchSelection)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "notes.md"), "before");
        await File.WriteAllTextAsync(Path.Combine(workspace, "other.md"), "other before");
        var owner = new ControlledTargetOwner();
        var view = await CreateEditorAsync(settings, workspace, owner);
        await SelectAndWaitAsync(view, "notes.md", "before");
        view.DraftProposedContent = "captured edit";

        var held = owner.ExecuteTargetAsync(workspace, "notes.md", _ => Task.FromResult(true));
        Task? save = null;
        try
        {
            await owner.FirstTargetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            save = view.SaveWorkspaceFileCommand.ExecuteAsync(null);
            await owner.SecondTargetRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(save.IsCompleted);

            if (switchSelection)
                await SelectAndWaitAsync(view, "other.md", "other before");
            view.DraftProposedContent = "newer unsaved edit";
            owner.ReleaseFirstTarget.TrySetResult();
            await held.WaitAsync(TimeSpan.FromSeconds(5));
            await save.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("captured edit", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
            Assert.Equal("other before", await File.ReadAllTextAsync(Path.Combine(workspace, "other.md")));
            Assert.Equal("newer unsaved edit", view.DraftProposedContent);
            Assert.True(view.WorkspaceEditorDirty);
            Assert.Equal(switchSelection ? "other before" : "captured edit", view.WorkspaceFilePreview);
            Assert.Equal(switchSelection ? "other.md" : "notes.md", view.SelectedWorkspaceFile!.RelativePath);

            // A subsequent explicit save must use the new base revision, not
            // falsely conflict or reuse the first request's captured text.
            await view.SaveWorkspaceFileCommand.ExecuteAsync(null);
            Assert.Equal("newer unsaved edit", await File.ReadAllTextAsync(
                Path.Combine(workspace, switchSelection ? "other.md" : "notes.md")));
            Assert.False(view.WorkspaceEditorDirty);
            Assert.False(view.IsError);
        }
        finally
        {
            owner.ReleaseFirstTarget.TrySetResult();
            await held.WaitAsync(TimeSpan.FromSeconds(5));
            if (save is not null) await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<AgentViewModel> CreateEditorAsync(
        Hermaeus.Core.Services.ISettingsService settings, string workspace, IAgentTaskCommandOwner owner)
    {
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var memory = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memory.InitializeAsync();
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var llm = new FakeLlm();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), llm, settings, new NoOpReranker());
        var manifests = new WorkspaceManifestService();
        var profiles = new FileWorkspaceProfileStore(settings);
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(),
            new AgentToolExecutor(tools), new FakeAgentLlm(), workspaceTools: tools, commandOwner: owner);
        return new AgentViewModel(agent, store, memory, tools, llm, rag,
            new RuntimeLogService(settings), new WorkspaceAnalysisService(profiles, memory),
            new WorkspaceActivationService(manifests, profiles), manifests, settings, commandOwner: owner)
        { WorkspaceRoot = workspace };
    }

    private static async Task SelectAndWaitAsync(AgentViewModel view, string path, string content)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(view.DraftProposedContent)
                && view.WorkspaceFilePreview == content && view.DraftProposedContent == content)
                loaded.TrySetResult();
        }
        view.PropertyChanged += OnChanged;
        try
        {
            view.SelectedWorkspaceFile = new AgentWorkspaceFileViewModel(path, "", null);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { view.PropertyChanged -= OnChanged; }
    }

    private sealed class ControlledTargetOwner : IAgentTaskCommandOwner
    {
        private readonly AgentTaskCommandOwner _owner = new();
        private int _targetRequests;
        public TaskCompletionSource FirstTargetEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondTargetRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstTarget { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExecuteTaskAsync(string taskId, Func<CancellationToken, Task> action, CancellationToken ct = default) =>
            _owner.ExecuteTaskAsync(taskId, action, ct);

        public Task<T> ExecuteTaskAsync<T>(string taskId, Func<CancellationToken, Task<T>> action, CancellationToken ct = default) =>
            _owner.ExecuteTaskAsync(taskId, action, ct);

        public Task<T> ExecuteTargetAsync<T>(string workspaceRoot, string relativePath,
            Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            var request = Interlocked.Increment(ref _targetRequests);
            if (request == 2) SecondTargetRequested.TrySetResult();
            return _owner.ExecuteTargetAsync(workspaceRoot, relativePath, async token =>
            {
                if (request == 1)
                {
                    FirstTargetEntered.TrySetResult();
                    await ReleaseFirstTarget.Task.WaitAsync(token);
                }
                return await action(token);
            }, ct);
        }
    }

    [Fact]
    public async Task Owner_save_writes_directly_and_returns_a_new_revision_hash()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");

        var tools = new AgentWorkspaceTools();
        var result = await tools.SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace),
            "notes.md",
            "after",
            AgentMutationPreparation.ComputeContentSha256("before"),
            expectedExisted: true);

        Assert.True(result.Changed);
        Assert.False(result.Conflict);
        Assert.Equal(AgentMutationPreparation.ComputeContentSha256("after"), result.ContentSha256);
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_refuses_to_overwrite_an_external_revision()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");
        var expectedHash = AgentMutationPreparation.ComputeContentSha256("before");
        await File.WriteAllTextAsync(path, "external");

        var result = await new AgentWorkspaceTools().SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace), "notes.md", "owner edit", expectedHash, expectedExisted: true);

        Assert.False(result.Changed);
        Assert.True(result.Conflict);
        Assert.Contains("Reload", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("external", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_rejects_unsafe_replacement_without_mutating_the_file()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");
        var expectedHash = AgentMutationPreparation.ComputeContentSha256("before");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentWorkspaceTools().SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace), "notes.md", "unsafe\0content", expectedHash, expectedExisted: true));

        Assert.Equal("before", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_makes_a_prepared_agent_patch_conflict_instead_of_clobbering_it()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "notes.md"), "before");
        var options = new AgentWorkspaceOptions(workspace);
        var task = new AgentTaskState
        {
            TaskId = "owner-save-stale-agent",
            Goal = "change notes",
            WorkspaceRoot = workspace,
            Status = AgentTaskStatus.Complete
        };
        await store.SaveAsync(task);

        var tools = new AgentWorkspaceTools();
        var review = new AgentPatchReviewService(tools, store);
        var patch = await review.QueueAsync(task.TaskId, "notes.md", "agent proposal", "agent", options);
        var ownerResult = await tools.SaveOwnerFileAsync(
            options,
            "notes.md",
            "owner edit",
            AgentMutationPreparation.ComputeContentSha256("before"),
            expectedExisted: true);

        var outcome = await review.ApplyAsync(task, patch, options);

        Assert.True(ownerResult.Changed);
        Assert.Equal(AgentMutationOutcome.Conflict, outcome);
        Assert.Equal("owner edit", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Equal(AgentDraftPatchStatus.Blocked, patch.Status);
    }
}
