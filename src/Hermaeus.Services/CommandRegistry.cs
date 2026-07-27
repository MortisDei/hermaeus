using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, AppCommand> _byId = new(StringComparer.Ordinal);
    private readonly List<AppCommand> _ordered = [];

    public void Register(AppCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Id))
            throw new InvalidOperationException("A command must have a non-empty Id.");
        if (!_byId.TryAdd(command.Id, command))
            throw new InvalidOperationException($"Command id '{command.Id}' is already registered.");

        _ordered.Add(command);
    }

    public IReadOnlyList<AppCommand> All => _ordered;

    public IReadOnlyList<AppCommand> ByArea(string area) =>
        _ordered.Where(c => string.Equals(c.Area, area, StringComparison.Ordinal)).ToList();
}
