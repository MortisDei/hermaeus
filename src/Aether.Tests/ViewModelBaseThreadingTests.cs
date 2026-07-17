using Xunit;

namespace Aether.Tests;

public sealed class ViewModelBaseThreadingTests
{
    private sealed class ProbeViewModel : Aether.ViewModels.ViewModelBase
    {
        public void CallRunOnUi(Action action) => RunOnUi(action);
        public Task CallRunOnUiAsync(Func<Task> action) => RunOnUiAsync(action);
    }

    [Fact]
    public void RunOnUi_executes_inline_when_already_on_the_captured_context()
    {
        var sync = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        try
        {
            var vm = new ProbeViewModel();
            var ran = false;

            vm.CallRunOnUi(() => ran = true);

            Assert.True(ran, "the action should have run");
            Assert.Equal(0, sync.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task RunOnUi_still_posts_when_called_from_a_different_thread()
    {
        var sync = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        Aether.ViewModels.ViewModelBase vm;
        try
        {
            vm = new ProbeViewModel();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var probe = (ProbeViewModel)vm;
        var ran = false;
        await Task.Run(() => probe.CallRunOnUi(() => ran = true));

        Assert.True(ran, "the action should still have run (posted then executed)");
        Assert.Equal(1, sync.PostCount);
    }

    [Fact]
    public void RunOnUiAsync_executes_inline_when_already_on_the_captured_context()
    {
        var sync = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        try
        {
            var vm = new ProbeViewModel();
            var ran = false;

            var task = vm.CallRunOnUiAsync(() => { ran = true; return Task.CompletedTask; });

            Assert.True(task.IsCompleted, "an inline action should complete synchronously");
            Assert.True(ran);
            Assert.Equal(0, sync.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
