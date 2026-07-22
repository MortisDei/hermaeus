using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

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

    /// <summary>
    /// True when the calling thread is already the one this instance
    /// captured at construction (or none was captured, i.e. headless tests).
    /// </summary>
    private bool IsOnCapturedContext => _sync is null || ReferenceEquals(SynchronizationContext.Current, _sync);

    protected void RunOnUi(Action action)
    {
        if (IsOnCapturedContext)
            action();
        else
            _sync!.Post(_ => action(), null);
    }

    protected Task RunOnUiAsync(Func<Task> action)
    {
        if (IsOnCapturedContext)
            return action();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sync!.Post(async _ =>
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
