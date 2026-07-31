namespace Hermaeus.Core.Models;

/// <summary>
/// Where an activity row points (r28 doc 03 3.1). Activity has always carried
/// the artifact's own identifier in <see cref="ActivityEvent.SourceId"/> and
/// the app has always had <see cref="RecallTarget"/> as its typed navigation
/// instruction; the two had simply never been introduced.
/// </summary>
/// <remarks>
/// Pure, static, no I/O, and deliberately dull: a prefix on the operation
/// picks the field, and an empty <c>SourceId</c> means no link. A row whose
/// operation is not one of these shows no link at all. It does not show a
/// disabled link and it does not guess.
/// </remarks>
public static class ActivityTargetResolver
{
    /// <summary>
    /// The operation prefixes that carry an identifier something can navigate
    /// to. Everything else (doctor, services, models, backup, voice) records
    /// work that has no single artifact to open, and is unmapped on purpose
    /// rather than by omission. <c>ActivityLinkTests</c> asserts every
    /// operation string actually in use is either here or in that list.
    /// </summary>
    public static IReadOnlyList<string> LinkedPrefixes { get; } = ["agent.", "rag.", "chat.", "memory."];

    public static RecallTarget? Resolve(ActivityEvent activity) => Resolve(activity.Operation, activity.SourceId);

    public static RecallTarget? Resolve(string operation, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(operation))
            return null;

        var id = sourceId.Trim();
        if (operation.StartsWith("agent.", StringComparison.OrdinalIgnoreCase))
            return new RecallTarget(TaskId: id);
        if (operation.StartsWith("rag.", StringComparison.OrdinalIgnoreCase))
            return new RecallTarget(DatasetId: id);
        if (operation.StartsWith("chat.", StringComparison.OrdinalIgnoreCase))
            return new RecallTarget(ConversationId: id);
        if (operation.StartsWith("memory.", StringComparison.OrdinalIgnoreCase))
            return new RecallTarget(MemoryId: id);

        return null;
    }

    /// <summary>
    /// The <see cref="RecallKind"/> a resolved target should be presented as,
    /// so the existing navigator can be reused rather than a second one grown
    /// beside it.
    /// </summary>
    public static RecallKind KindFor(RecallTarget target) => target switch
    {
        { TaskId.Length: > 0 } => RecallKind.Task,
        { DatasetId.Length: > 0 } => RecallKind.Document,
        { MemoryId.Length: > 0 } => RecallKind.Memory,
        _ => RecallKind.Message
    };
}
