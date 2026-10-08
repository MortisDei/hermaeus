using Avalonia;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Xunit;

namespace Hermaeus.Tests;

[CollectionDefinition(AvaloniaTestHost.CollectionName)]
public sealed class AvaloniaTestCollection : ICollectionFixture<AvaloniaTestHost> { }

// One native runtime and UI thread for control and image tests. A plain
// Application avoids Hermaeus startup, owner settings and service composition.
public sealed class AvaloniaTestHost : IDisposable
{
    public const string CollectionName = "Native Avalonia";
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    public AvaloniaTestHost()
    {
        _thread = new Thread(RunDispatcher) { IsBackground = true, Name = "Hermaeus test UI" };
        if (OperatingSystem.IsWindows())
            _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.InvokeAsync(action);
    }

    private void RunDispatcher()
    {
        try
        {
            Hermaeus.Desktop.Program.BuildAvaloniaApp<Application>().SetupWithoutStarting();
            Application.Current!.Styles.Add(new FluentTheme());
            _ready.TrySetResult(Dispatcher.UIThread);
            Dispatcher.UIThread.MainLoop(_shutdown.Token);
            _stopped.TrySetResult();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            _stopped.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        Assert.True(_thread.Join(TimeSpan.FromSeconds(10)), "The native test UI thread did not stop.");
        _shutdown.Dispose();
        _stopped.Task.GetAwaiter().GetResult();
    }
}
