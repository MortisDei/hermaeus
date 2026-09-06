using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r17 02-benchmark-truth.md 2.7: merely browsing the Benchmarks model dropdown used to stop
/// and restart the live managed chat server (a 1-2 minute operation on large models) before
/// Run was ever clicked. The counter here watches the real ServicesViewModel's server-status
/// transitions - every restart attempt passes through ServerStatus.Starting even when it then
/// immediately errors on an unset executable path, so counting that transition is a reliable
/// "was a restart attempted" seam without needing a real llama-server binary.
/// </summary>
public sealed class BenchmarkViewModelModelSelectionTests
{
    [Fact]
    public async Task Selecting_a_model_triggers_no_restart_and_running_triggers_exactly_one()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        // This test only observes the attempted start. Keep it out of the
        // real network: an occupied hosted-runner port would be rejected by
        // port preflight before the intended Starting transition.
        const int chatPort = 0;
        const int embeddingsPort = 0;
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = string.Empty, Port = chatPort });
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Embeddings", ExecutablePath = string.Empty, Port = embeddingsPort, EmbeddingsMode = true });

        var services = NewServicesViewModel(settings);
        Assert.Equal(chatPort, services.Servers[0].Port);
        var starts = 0;
        services.Servers[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerProcessViewModel.Status) && services.Servers[0].Status == ServerStatus.Starting)
                starts++;
        };

        var llm = new FakeLlm();
        var benchmarks = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());
        var vm = new BenchmarkViewModel(benchmarks, llm, new ModelProfileService(settings), settings, new FakeToasts(), services: services);
        await vm.LoadAsync();

        var localModel = new LlmModel { Id = modelPath, Name = "Local", Provider = "local GGUF", ProviderTag = "llama.cpp" };

        vm.SelectedModel = localModel;

        Assert.Equal(0, starts);
        Assert.Equal(string.Empty, services.Servers[0].ModelPath);
        Assert.Contains("when the benchmark runs", vm.Status, StringComparison.Ordinal);

        var suite = BenchmarkService.StarterSuites().First();
        suite.MaxCases = 1;
        vm.SelectedSuite = suite;

        await vm.RunCommand.ExecuteAsync(null);

        // The selected runtime is intentionally unconfigured. Batch 1 rejects
        // its unproven placement before entering Starting, so this remains an
        // attempted run without claiming that a child process was launched.
        await WaitForAsync(
            () => services.Servers[0].Status == ServerStatus.Error,
            "the intentionally unconfigured managed server restart to settle");
        Assert.Equal(Path.GetFullPath(modelPath), services.Servers[0].ModelPath);
        Assert.Equal(0, starts);
    }
}
