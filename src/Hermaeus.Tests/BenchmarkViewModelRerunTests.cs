using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.8 (Rerun lacked an IsRunning guard,
/// letting a second click during a run overwrite the CTS and leak the old
/// one) and 02-async-and-threading.md 2.5 (LoadAsync re-entrancy).
/// </summary>
public sealed class BenchmarkViewModelRerunTests
{
    private static async Task<BenchmarkViewModel> NewViewModelAsync(TempDir temp, ILlmService llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());
        var vm = new BenchmarkViewModel(benchmarks, llm, new ModelProfileService(settings), settings, new FakeToasts());
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task Rerun_is_disabled_while_a_run_is_already_in_progress()
    {
        using var temp = new TempDir();
        var vm = await NewViewModelAsync(temp, new FakeLlm());

        vm.IsRunning = true;

        Assert.False(vm.RerunCommand.CanExecute(null), "Rerun must be disabled while IsRunning");
    }

    [Fact]
    public async Task Concurrent_LoadAsync_calls_share_the_in_flight_load_and_never_duplicate_suites()
    {
        using var temp = new TempDir();
        var gate = new TaskCompletionSource();
        var llm = new ScriptedModelsLlm(() => [new LlmModel { Id = "a", Name = "a", Provider = "Test" }]) { DelayGate = gate };
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());
        var vm = new BenchmarkViewModel(benchmarks, llm, new ModelProfileService(settings), settings, new FakeToasts());

        var first = vm.LoadAsync();
        var second = vm.LoadAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, llm.GetModelsCallCount);
    }
}
