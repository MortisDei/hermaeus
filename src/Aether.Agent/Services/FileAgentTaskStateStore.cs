using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public sealed class FileAgentTaskStateStore : IAgentTaskStateStore
{
    private readonly ISettingsService _settings;

    public FileAgentTaskStateStore(ISettingsService settings)
    {
        _settings = settings;
    }

    private string AgentRoot
    {
        get
        {
            var configured = _settings.Settings.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
                : Path.GetFullPath(configured);
            return Path.Combine(root, "agent");
        }
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.Combine(AgentRoot, "tasks"));
        return Task.CompletedTask;
    }

    public string GetTaskDirectory(string taskId)
    {
        var safeId = Path.GetFileName(taskId);
        return Path.Combine(AgentRoot, "tasks", safeId);
    }

    public async Task SaveAsync(AgentTaskState state, CancellationToken ct = default)
    {
        state.UpdatedAt = DateTime.UtcNow;
        var dir = GetTaskDirectory(state.TaskId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "task_state.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, AgentJson.Options), ct);
    }

    public async Task<AgentTaskState?> LoadAsync(string taskId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetTaskDirectory(taskId), "task_state.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<AgentTaskState>(json, AgentJson.Options);
    }

    public async Task<IReadOnlyList<AgentTaskListItem>> ListRecentAsync(int limit = 25, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var tasks = new List<AgentTaskListItem>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AgentRoot, "tasks"), "task_state.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var state = JsonSerializer.Deserialize<AgentTaskState>(json, AgentJson.Options);
                if (state is not null)
                    tasks.Add(new AgentTaskListItem(state.TaskId, state.Goal, state.Status, state.UpdatedAt));
            }
            catch
            {
                // Ignore corrupt task state entries so one bad task cannot hide the rest.
            }
        }

        return tasks
            .OrderByDescending(t => t.UpdatedAt)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public async Task AppendLogAsync(string taskId, string line, CancellationToken ct = default)
    {
        var dir = GetTaskDirectory(taskId);
        Directory.CreateDirectory(dir);
        await File.AppendAllTextAsync(Path.Combine(dir, "agent.log"), $"{DateTime.UtcNow:O} {line}{Environment.NewLine}", ct);
    }

    public async Task AppendTraceAsync(string taskId, object trace, CancellationToken ct = default)
    {
        var dir = GetTaskDirectory(taskId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(trace, AgentJson.Options);
        await File.AppendAllTextAsync(Path.Combine(dir, "agent.trace.jsonl"), json + Environment.NewLine, ct);
    }
}
