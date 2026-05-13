using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IRuntimeLogService
{
    event Action<RuntimeLogEntry> LogAdded;
    void Add(RuntimeLogEntry entry);
    IReadOnlyList<RuntimeLogEntry> GetEntries();
    void ClearInMemory();
    string GetLogDirectory();
    string GetLogFilePath();
}
