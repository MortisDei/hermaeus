using System.IO.Pipes;
using Hermaeus.Desktop;
using Xunit;

namespace Hermaeus.Tests;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public async Task Second_launch_activates_existing_owner_without_releasing_its_lock()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");
        Assert.True(SingleInstanceGuard.TryAcquire(lockPath));
        try
        {
            using var activation = new SingleInstanceActivation(lockPath);
            var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            activation.SetHandler(() => activated.TrySetResult());
            Assert.True(await SingleInstanceActivation.TryRequestAsync(lockPath));
            await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(SingleInstanceGuard.TryAcquire(lockPath));
        }
        finally { SingleInstanceGuard.Release(); }
    }

    [Fact]
    public async Task Activation_during_startup_is_retained_until_the_window_handler_is_ready()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");
        using var activation = new SingleInstanceActivation(lockPath);
        Assert.True(await SingleInstanceActivation.TryRequestAsync(lockPath));
        var calls = 0;
        activation.SetHandler(() => Interlocked.Increment(ref calls));
        Assert.Equal(1, calls);
        Assert.True(await SingleInstanceActivation.TryRequestAsync(lockPath));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Invalid_and_disconnected_clients_do_not_disable_later_activation()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");
        using var activation = new SingleInstanceActivation(lockPath);
        var calls = 0;
        activation.SetHandler(() => Interlocked.Increment(ref calls));
        await using (var client = new NamedPipeClientStream(".", SingleInstanceActivation.PipeName(lockPath),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(new byte[] { 42 }, timeout.Token);
            Assert.Equal(0, await client.ReadAsync(new byte[1], timeout.Token));
        }
        Assert.Equal(0, Volatile.Read(ref calls));
        Assert.True(await SingleInstanceActivation.TryRequestAsync(lockPath));
        Assert.Equal(1, Volatile.Read(ref calls));

        await using (var client = new NamedPipeClientStream(".", SingleInstanceActivation.PipeName(lockPath),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeout.Token);
            // Disconnect before sending anything, exercising the EOF/broken-pipe path.
        }
        Assert.True(await SingleInstanceActivation.TryRequestAsync(lockPath));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Missing_owner_times_out_without_starting_a_second_desktop()
    {
        using var temp = new TempDir();
        Assert.False(await SingleInstanceActivation.TryRequestAsync(temp.PathFor("absent.lock"),
            TimeSpan.FromMilliseconds(50)));
    }
}
