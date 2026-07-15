using System.Collections.ObjectModel;

namespace Aether.ViewModels;

/// <summary>
/// Guard for collections and state that must only be touched on the UI thread.
/// The desktop app arms it once on the Avalonia UI thread at startup; headless
/// tests never arm it, so it is inert there. Armed, any mutation of a
/// <see cref="UiBoundCollection{T}"/> from another thread throws immediately
/// with the offending call in the stack, instead of corrupting Avalonia's
/// ItemsControl container generator and crashing at an arbitrary later point
/// with "Collection was modified".
/// </summary>
public static class UiThreadGuard
{
    private static int _ownerThreadId = -1;

    /// <summary>Call once from the UI thread before any view is shown.</summary>
    public static void Arm() => _ownerThreadId = Environment.CurrentManagedThreadId;

    internal static void AssertUiThread()
    {
        var owner = _ownerThreadId;
        if (owner != -1 && Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException(
                $"UI-bound collection mutated from thread {Environment.CurrentManagedThreadId} " +
                $"but the UI thread is {owner}. Marshal the mutation to the UI thread " +
                "(see RunOnUi in the owning view model).");
    }
}

/// <summary>
/// ObservableCollection for collections bound to Avalonia ItemsControls.
/// Enforces UI-thread-only mutation via <see cref="UiThreadGuard"/>.
/// </summary>
public sealed class UiBoundCollection<T> : ObservableCollection<T>
{
    protected override void InsertItem(int index, T item)
    {
        UiThreadGuard.AssertUiThread();
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        UiThreadGuard.AssertUiThread();
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, T item)
    {
        UiThreadGuard.AssertUiThread();
        base.SetItem(index, item);
    }

    protected override void ClearItems()
    {
        UiThreadGuard.AssertUiThread();
        base.ClearItems();
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        UiThreadGuard.AssertUiThread();
        base.MoveItem(oldIndex, newIndex);
    }
}
