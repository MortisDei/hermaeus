using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class ConversationMemoryService : IConversationMemoryService
{
    private const int MaxAutoSummaryCacheEntries = 500;
    private static readonly string[] ImportanceKeywords =
    [
        "prefer", "like", "dislike", "always", "never", "important", "remember", "goal", "workflow",
        "project", "deadline", "bug", "performance", "privacy", "security"
    ];

    private readonly ISettingsService _settings;
    private readonly IConversationStore _conversations;
    private readonly IMemoryStore _memories;
    private readonly MemoryExtractionService _extractor;
    private readonly ILlmService _llm;
    private readonly IRuntimeLogService _logs;
    private readonly object _summaryCacheLock = new();
    private readonly Dictionary<string, DateTime> _lastAutoSummaryByConversation = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _summaryCacheOrder = new();

    public ConversationMemoryService(
        ISettingsService settings,
        IConversationStore conversations,
        IMemoryStore memories,
        MemoryExtractionService extractor,
        ILlmService llm,
        IRuntimeLogService logs)
    {
        _settings = settings;
        _conversations = conversations;
        _memories = memories;
        _extractor = extractor;
        _llm = llm;
        _logs = logs;
    }

    public async Task RunAutoSummaryAsync(string conversationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var memorySettings = _settings.Settings.Memory;
        if (!memorySettings.Enabled)
            return;

        var conversation = await _conversations.GetByIdAsync(conversationId, ct);
        if (conversation is null)
            return;

        if (!IsEligibleForSummary(conversation))
            return;

        var importance = EstimateConversationImportance(conversation);
        if (importance < memorySettings.AutoSummarizeImportanceThreshold)
            return;

        if (await HasRecentAutoSummaryAsync(conversationId, ct))
            return;

        var modelId = await ResolveModelIdAsync(conversation.ModelId, ct);
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        var summaryOutput = await GenerateSummaryOutputAsync(modelId, conversation, ct);
        var extracted = await _extractor.ExtractMemoriesAsync(summaryOutput, conversationId);
        if (extracted.Count == 0)
            return;

        var picked = extracted
            .OrderByDescending(m => m.ImportanceScore)
            .Take(5)
            .ToList();

        foreach (var memory in picked)
        {
            memory.ImportanceScore = Math.Clamp((memory.ImportanceScore + importance) / 2.0, 0, 1);
            memory.Tags = memory.Tags
                .Append("auto_summary")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (memorySettings.AutoArchiveAfterDays > 0)
                memory.ExpirationDate = DateTime.UtcNow.AddDays(memorySettings.AutoArchiveAfterDays);
        }

        await MergeAndSaveAsync(picked, conversationId, ct);
        await EnforcePerConversationCapAsync(conversationId, memorySettings.MaxMemoriesPerConversation, ct);
        MarkAutoSummary(conversationId, DateTime.UtcNow);
    }

    private static bool IsEligibleForSummary(Conversation conversation)
    {
        if (conversation.Messages.Count < 4)
            return false;

        var userMessages = conversation.Messages.Count(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content));
        return userMessages >= 2;
    }

    private static double EstimateConversationImportance(Conversation conversation)
    {
        var userMessages = conversation.Messages
            .Where(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content))
            .ToList();
        if (userMessages.Count == 0)
            return 0;

        var avgLength = userMessages.Average(m => m.Content.Length);
        var lengthScore = Math.Clamp(avgLength / 280.0, 0, 1);

        var keywordHits = userMessages.Sum(m => ImportanceKeywords.Count(k =>
            m.Content.Contains(k, StringComparison.OrdinalIgnoreCase)));
        var keywordScore = Math.Clamp(keywordHits / 6.0, 0, 1);

        var interactionScore = Math.Clamp(conversation.Messages.Count / 20.0, 0, 1);
        var score = (lengthScore * 0.35) + (keywordScore * 0.45) + (interactionScore * 0.20);
        return Math.Round(Math.Clamp(score, 0, 1), 3);
    }

    private async Task<bool> HasRecentAutoSummaryAsync(string conversationId, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-6);
        lock (_summaryCacheLock)
        {
            if (_lastAutoSummaryByConversation.TryGetValue(conversationId, out var cached) && cached >= cutoff)
            {
                TouchSummaryCacheUnsafe(conversationId);
                return true;
            }
        }

        var recent = await _memories.GetRecentByConversationAsync(conversationId, 20, ct);
        var latest = recent
            .Where(m =>
            m.UpdatedAt >= cutoff
            && m.Tags.Any(t => string.Equals(t, "auto_summary", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.UpdatedAt)
            .FirstOrDefault();
        if (latest is null)
            return false;

        MarkAutoSummary(conversationId, latest.UpdatedAt);
        return true;
    }

    private void MarkAutoSummary(string conversationId, DateTime timestamp)
    {
        lock (_summaryCacheLock)
        {
            _lastAutoSummaryByConversation[conversationId] = timestamp;
            TouchSummaryCacheUnsafe(conversationId);
            while (_lastAutoSummaryByConversation.Count > MaxAutoSummaryCacheEntries && _summaryCacheOrder.First is not null)
            {
                var oldest = _summaryCacheOrder.First.Value;
                _summaryCacheOrder.RemoveFirst();
                _lastAutoSummaryByConversation.Remove(oldest);
            }
        }
    }

    private void TouchSummaryCacheUnsafe(string conversationId)
    {
        var existing = _summaryCacheOrder.Find(conversationId);
        if (existing is not null)
            _summaryCacheOrder.Remove(existing);
        _summaryCacheOrder.AddLast(conversationId);
    }

    private async Task<string> ResolveModelIdAsync(string conversationModelId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(conversationModelId))
            return conversationModelId;

        if (!string.IsNullOrWhiteSpace(_settings.Settings.Llm.DefaultModel))
            return _settings.Settings.Llm.DefaultModel;

        var models = await _llm.GetModelsAsync(ct);
        return models.FirstOrDefault()?.Id ?? string.Empty;
    }

    private async Task<string> GenerateSummaryOutputAsync(string modelId, Conversation conversation, CancellationToken ct)
    {
        var transcript = BuildTranscript(conversation.Messages, maxMessages: 14, maxCharsPerMessage: 500);
        var prompt = BuildSummaryPrompt(transcript);
        var buffer = new StringBuilder();

        await foreach (var token in _llm.StreamChatTextAsync(
                           modelId,
                           [new ChatMessage("user", prompt)],
                           new LlmChatOptions
                           {
                               SystemPrompt = "You are a memory extraction assistant. Follow the output format exactly.",
                               Temperature = 0.2
                           },
                           ct))
        {
            buffer.Append(token);
        }

        return buffer.ToString();
    }

    private static string BuildTranscript(List<Message> messages, int maxMessages, int maxCharsPerMessage)
    {
        var picked = messages
            .Where(m => (m.Role == "user" || m.Role == "assistant") && !m.IsError)
            .TakeLast(maxMessages)
            .Select(m =>
            {
                var text = (m.Content ?? string.Empty).Trim();
                if (text.Length > maxCharsPerMessage)
                    text = text[..maxCharsPerMessage] + "...";
                return $"[{m.Role}] {text}";
            });

        return string.Join("\n", picked);
    }

    private static string BuildSummaryPrompt(string transcript)
    {
        return $"""
                Summarise durable user memory from this conversation.

                Output requirements:
                - Output only memory markers in this exact format: [MEMORY: <content>]
                - Maximum 5 memories
                - Include only durable details: preferences, stable goals, recurring constraints, long-lived facts, learned behaviour patterns
                - Do not include temporary one-off tasks or transient status
                - If nothing durable exists, return nothing

                Conversation transcript:
                {transcript}
                """;
    }

    private async Task MergeAndSaveAsync(List<Memory> memories, string conversationId, CancellationToken ct)
    {
        var existing = await _memories.GetAllAsync(includeArchived: true, ct);

        foreach (var memory in memories)
        {
            memory.SourceConversationId ??= conversationId;
            memory.Source ??= new SourceReference(ProvenanceKind.Memory, MemoryExtractionService.TitleFrom(memory.Content), Locator: conversationId, Snippet: memory.Content, Timestamp: DateTime.UtcNow);
            var duplicate = existing.FirstOrDefault(m =>
                string.Equals(m.Category, memory.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(m.Content), Normalize(memory.Content), StringComparison.Ordinal));

            if (duplicate is null)
            {
                await _memories.SaveAsync(memory, ct);
                existing.Add(memory);
                continue;
            }

            duplicate.FrequencyCount += 1;
            duplicate.LastMergeTime = DateTime.UtcNow;
            duplicate.UpdatedAt = DateTime.UtcNow;
            duplicate.ImportanceScore = Math.Max(duplicate.ImportanceScore, memory.ImportanceScore);
            duplicate.Tags = duplicate.Tags
                .Concat(memory.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            duplicate.SourceConversationId ??= conversationId;
            duplicate.Source ??= new SourceReference(ProvenanceKind.Memory, MemoryExtractionService.TitleFrom(duplicate.Content), Locator: conversationId, Snippet: duplicate.Content, Timestamp: DateTime.UtcNow);
            await _memories.SaveAsync(duplicate, ct);
        }
    }

    private async Task EnforcePerConversationCapAsync(string conversationId, int maxMemoriesPerConversation, CancellationToken ct)
    {
        if (maxMemoriesPerConversation <= 0)
            return;

        var all = await _memories.GetAllAsync(includeArchived: true, ct);
        var scoped = all
            .Where(m => string.Equals(m.SourceConversationId, conversationId, StringComparison.Ordinal))
            .OrderByDescending(m => m.IsPinned)
            .ThenByDescending(m => m.UpdatedAt)
            .ToList();

        if (scoped.Count <= maxMemoriesPerConversation)
            return;

        foreach (var memory in scoped.Skip(maxMemoriesPerConversation).Where(m => !m.IsPinned && !m.IsArchived))
        {
            memory.IsArchived = true;
            await _memories.SaveAsync(memory, ct);
        }

        _logs.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Service,
            $"Auto-summary archived excess memories for conversation {conversationId}."));
    }

    private static string Normalize(string content)
    {
        return string.Join(' ', content
            .Trim()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
