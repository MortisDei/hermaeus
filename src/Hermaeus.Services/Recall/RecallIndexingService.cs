using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.Recall;

/// <summary>
/// r24 doc 02: the only writer of recall.db. Every write path here checks
/// <see cref="MemorySettings.RecallIndexingEnabled"/> first (2.0's switch),
/// so turning the switch off stops indexing immediately, and none of this
/// ever runs on the chat send path (doc 06 "nothing goes on the send path").
/// </summary>
public sealed class RecallIndexingService
{
    /// <summary>A bare "thanks" is noise that would otherwise dominate short-query results (doc 02 2.2).</summary>
    private const int MinMessageChars = 8;

    private readonly RecallIndexStore _index;
    private readonly ISettingsService _settings;

    public RecallIndexingService(RecallIndexStore index, ISettingsService settings)
    {
        _index = index;
        _settings = settings;
    }

    private bool Enabled => _settings.Settings.Memory.RecallIndexingEnabled;

    /// <summary>Incremental: called on conversation save. Upserts that conversation's
    /// messages, or removes them entirely if the conversation is excluded.</summary>
    public async Task IndexConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        if (!Enabled) return;

        if (conversation.RecallExcluded)
        {
            await _index.DeleteBySourceAsync("message", conversation.Id, ct);
            return;
        }

        var entries = new List<RecallEntry>();
        for (var i = 0; i < conversation.Messages.Count; i++)
        {
            var m = conversation.Messages[i];
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;
            var body = (m.Content ?? string.Empty).Trim();
            if (body.Length < MinMessageChars) continue;

            entries.Add(new RecallEntry
            {
                Id = RecallIndexStore.MakeId("message", conversation.Id, i.ToString()),
                Kind = "message",
                SourceId = conversation.Id,
                SubId = i.ToString(),
                ProjectId = conversation.ProjectId,
                Title = conversation.Title,
                Body = body,
                IsArchived = conversation.IsArchived,
                CreatedAt = conversation.CreatedAt
            });
        }

        if (entries.Count > 0)
            await _index.UpsertBatchAsync(entries, ct);
    }

    /// <summary>Deletion propagates, always: deleting a conversation deletes its recall
    /// entries in the same operation.</summary>
    public async Task RemoveConversationAsync(string conversationId, CancellationToken ct = default) =>
        await _index.DeleteBySourceAsync("message", conversationId, ct);

    /// <summary>Called on task terminal-state transitions and on report write, never on
    /// every step (doc 02 2.3).</summary>
    public async Task IndexTaskAsync(RecallTaskInput input, CancellationToken ct = default)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(input.Body)) return;

        var entry = new RecallEntry
        {
            Id = RecallIndexStore.MakeId("task", input.TaskId, string.Empty),
            Kind = "task",
            SourceId = input.TaskId,
            SubId = input.ParentTaskId ?? string.Empty,
            ProjectId = input.ProjectId,
            Title = input.Goal,
            Body = input.Body,
            CreatedAt = input.CreatedAt
        };
        await _index.UpsertBatchAsync([entry], ct);
    }

    public async Task RemoveTaskAsync(string taskId, CancellationToken ct = default) =>
        await _index.DeleteBySourceAsync("task", taskId, ct);

    /// <summary>2.0's destructive control: deletes every row and vacuums, leaving the
    /// conversation/memory/task/dataset stores byte-identical.</summary>
    public Task<int> ClearIndexAsync(CancellationToken ct = default) => _index.ClearAsync(ct);

    public Task<(int Count, long Bytes)> GetSizeAsync(CancellationToken ct = default) => _index.GetSizeAsync(ct);

    /// <summary>Catches up conversations and tasks that existed before Recall shipped or
    /// before indexing was turned on. Bounded batch, modeled on MemoryStore's embedding
    /// backfill shape; call shortly after startup, never on the send path.</summary>
    public async Task RunStartupBackfillAsync(
        IReadOnlyList<Conversation> conversations,
        IReadOnlyList<RecallTaskInput> tasks,
        CancellationToken ct = default)
    {
        if (!Enabled) return;

        var indexedConvIds = await _index.GetIndexedSourceIdsAsync("message", ct);
        foreach (var conv in conversations.Where(c => !c.RecallExcluded && !indexedConvIds.Contains(c.Id)).Take(50))
        {
            ct.ThrowIfCancellationRequested();
            await IndexConversationAsync(conv, ct);
        }

        var indexedTaskIds = await _index.GetIndexedSourceIdsAsync("task", ct);
        foreach (var task in tasks.Where(t => !indexedTaskIds.Contains(t.TaskId)).Take(50))
        {
            ct.ThrowIfCancellationRequested();
            await IndexTaskAsync(task, ct);
        }

        await _index.RunEmbeddingBackfillAsync(ct);
    }
}
