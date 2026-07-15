using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

/// <summary>
/// Base for ViewModels that receive events from services on non-UI threads
/// (process status, log lines, background progress). Captures the UI
/// <see cref="SynchronizationContext"/> at construction time so writers can
/// marshal state changes back onto it. When no context is present (headless
/// tests), actions run inline so tests stay synchronous.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;

    protected void RunOnUi(Action action)
    {
        if (_sync is null)
            action();
        else
            _sync.Post(_ => action(), null);
    }

    protected Task RunOnUiAsync(Func<Task> action)
    {
        if (_sync is null)
            return action();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sync.Post(async _ =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }
}
