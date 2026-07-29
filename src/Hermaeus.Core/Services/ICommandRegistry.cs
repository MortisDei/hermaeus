using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// One registry, two surfaces (doc 04 4.1): the palette (doc 02 2.5) and
/// per-panel discovery (doc 04 4.4) both read <see cref="All"/> instead of
/// keeping their own list. Populated once at composition time by each
/// ViewModel's own RegisterCommands method, not by one giant central file.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>Throws if a command with the same Id is already registered - a
    /// duplicate Id is a bug at registration time, not a runtime condition to tolerate.</summary>
    void Register(AppCommand command);
    IReadOnlyList<AppCommand> All { get; }
    IReadOnlyList<AppCommand> ByArea(string area);
}
