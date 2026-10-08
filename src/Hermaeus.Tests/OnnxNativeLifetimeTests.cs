using System.Reflection;
using Hermaeus.Voice;
using Hermaeus.Rag.Retrieval;
using Xunit;

namespace Hermaeus.Tests;

public sealed class OnnxNativeLifetimeTests
{
    [Fact]
    public async Task Disposal_waits_for_session_gate_and_rejects_later_work()
    {
        using var temp = new TempDir();
        var model = new KokoroOnnxModel(() => temp.PathFor("assets"));
        var gate = (SemaphoreSlim)typeof(KokoroOnnxModel)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
        await gate.WaitAsync();
        var disposal = model.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        gate.Release();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await model.DisposeAsync();
        Assert.False(model.IsLoaded);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureLoadedAsync("af_heart", CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.InstallAssetsAsync([], null, CancellationToken.None));
        Assert.Throws<ObjectDisposedException>(() => model.Synthesize([], "af_heart", 1));
    }

    [Fact]
    public async Task Reranker_disposal_joins_same_gate_as_load_and_scoring()
    {
        using var temp = new TempDir();
        var model = new OnnxCrossEncoderReranker(Helpers.NewSettings(temp));
        var gate = (SemaphoreSlim)typeof(OnnxCrossEncoderReranker)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
        await gate.WaitAsync();
        var disposal = model.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        gate.Release();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await model.DisposeAsync();
        Assert.False(model.IsLoaded);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.InstallAssetsAsync());
    }
}
