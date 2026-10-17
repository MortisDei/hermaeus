using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class MediaGenerationViewModelTests
{
    [Fact]
    public async Task Disabled_provider_is_not_called()
    {
        using var temp = new TempDir();
        var service = new ControlledService();
        var vm = new MediaGenerationViewModel(service, NewSettings(temp));
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Null(service.Request);
        Assert.Contains("Enable", vm.Status);
    }

    [Fact]
    public async Task Request_snapshots_config_and_cancellation_refuses_a_late_success()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.ComfyUiEnabled = true;
        settings.Settings.Llm.ComfyUiBaseUrl = "http://127.0.0.1:8188";
        settings.Settings.Llm.ComfyUiApiKey = "secret:original";
        var service = new ControlledService();
        var vm = new MediaGenerationViewModel(service, settings) { Prompt = "first" };
        var pending = vm.GenerateCommand.ExecuteAsync(null);
        Assert.True(vm.IsGenerating);
        Assert.False(vm.GenerateCommand.CanExecute(null));
        settings.Settings.Llm.ComfyUiBaseUrl = "https://changed.example.test";
        settings.Settings.Llm.ComfyUiApiKey = "secret:changed";
        Assert.Equal("http://127.0.0.1:8188", service.Request!.Endpoint);
        Assert.Equal("secret:original", service.Request.SecretReference);
        service.Progress!.Report(new("queued", "job-1"));
        vm.CancelCommand.Execute(null);
        service.Completion.SetResult(new([new("generated.png", "image/png", [1, 2, 3])], "job-1"));
        await pending;
        Assert.Empty(vm.Outputs);
        Assert.Null(vm.SelectedOutput);
        Assert.Equal("job-1", vm.JobId);
        Assert.Contains("may still be running", vm.Status);
        var status = vm.Status;
        service.Progress.Report(new("stale completion", "wrong-job"));
        Assert.Equal(status, vm.Status);
        Assert.Equal("job-1", vm.JobId);
    }

    [Fact]
    public async Task Successful_result_can_be_exported_exactly_and_cleared_without_deleting_export()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.ComfyUiEnabled = true;
        var service = new ControlledService();
        var vm = new MediaGenerationViewModel(service, settings) { Prompt = "synthetic" };
        var pending = vm.GenerateCommand.ExecuteAsync(null);
        var media = new GeneratedMedia("generated-1.wav", "audio/wav", "RIFF0000WAVEsynthetic"u8.ToArray());
        service.Completion.SetResult(new([media], "job-1"));
        await pending;
        Assert.True(vm.HasOutputs);
        Assert.Same(media, vm.SelectedOutput);
        var path = temp.PathFor("saved.wav");
        vm.RequestExportPath = _ => Task.FromResult<string?>(path);
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(media.Data, await File.ReadAllBytesAsync(path));
        vm.ClearCommand.Execute(null);
        Assert.False(vm.HasOutputs);
        Assert.Null(vm.SelectedOutput);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Shutdown_joins_generation_and_does_not_publish_late_results()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.ComfyUiEnabled = true;
        var service = new ControlledService();
        var vm = new MediaGenerationViewModel(service, settings) { Prompt = "synthetic" };
        var pending = vm.GenerateCommand.ExecuteAsync(null);
        var shutdown = vm.ShutdownAsync();
        Assert.True(service.Token.IsCancellationRequested);
        Assert.False(shutdown.IsCompleted);
        service.Completion.SetResult(new([new("generated-1.png", "image/png", [1])], "job-1"));
        await shutdown;
        await pending;
        Assert.Empty(vm.Outputs);
        Assert.False(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Comfy_configuration_uses_existing_settings_and_secret_flow()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        try
        {
            vm.Llm.ComfyUiEnabled = true;
            vm.Llm.ComfyUiBaseUrl = "http://127.0.0.1:8189";
            vm.Llm.ComfyUiApiKey = "synthetic-private-key";
            await vm.SaveAsync();
            Assert.Equal("secret:comfyui-api-key", settings.Settings.Llm.ComfyUiApiKey);
            var reloaded = NewSettings(temp);
            await reloaded.LoadAsync();
            Assert.Equal("secret:comfyui-api-key", reloaded.Settings.Llm.ComfyUiApiKey);
            Assert.Equal("http://127.0.0.1:8189", reloaded.Settings.Llm.ComfyUiBaseUrl);
            Assert.True(reloaded.Settings.Llm.ComfyUiEnabled);
            Assert.DoesNotContain("synthetic-private-key", await File.ReadAllTextAsync(temp.PathFor("settings/settings.json")));
            vm.Reload();
            Assert.Equal("", vm.Llm.ComfyUiApiKey);
            await vm.SaveAsync();
            Assert.Equal("secret:comfyui-api-key", settings.Settings.Llm.ComfyUiApiKey);
        }
        finally { await vm.ShutdownAsync(); }
    }

    private sealed class ControlledService : IMediaGenerationService
    {
        public MediaGenerationRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public IProgress<MediaGenerationProgress>? Progress { get; private set; }
        public TaskCompletionSource<MediaGenerationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MediaGenerationResult> GenerateAsync(MediaGenerationRequest request, IProgress<MediaGenerationProgress>? progress = null, CancellationToken ct = default)
        { Request = request; Token = ct; Progress = progress; return Completion.Task; }
    }
}
