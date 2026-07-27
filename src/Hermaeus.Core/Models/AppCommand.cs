namespace Hermaeus.Core.Models;

/// <summary>
/// One entry in the app's command registry (r24 doc 04 4.1): a single
/// user-facing action, navigation or otherwise. The palette (doc 02 2.5) and
/// per-panel discovery (doc 04 4.4) both read the same registry so they
/// cannot drift.
/// </summary>
public sealed record AppCommand(
    string Id,
    string Title,
    string Area,
    string Description,
    IReadOnlyList<string> Keywords,
    string Shortcut,
    Func<bool> CanExecute,
    Func<Task> Execute,
    /// <summary>Called only when <see cref="CanExecute"/> is false, so the
    /// palette can show why ("no workspace root selected", "server not
    /// running") instead of just hiding the entry.</summary>
    Func<string>? DisabledReason = null)
{
    public string ReasonUnavailable() => CanExecute() ? string.Empty : DisabledReason?.Invoke() ?? "Not available right now.";
}
