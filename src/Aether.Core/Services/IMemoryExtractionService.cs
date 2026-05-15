using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Service for extracting memories from model output and categorizing them.
/// </summary>
public interface IMemoryExtractionService
{
    /// <summary>
    /// Extract memories from model output.
    /// Looks for markers like [MEMORY: content] and auto-categorizes them.
    /// </summary>
    /// <param name="modelOutput">The full text response from the model</param>
    /// <param name="sourceConversationId">Optional reference to source conversation</param>
    /// <returns>List of extracted Memory objects ready to persist</returns>
    Task<List<Memory>> ExtractMemoriesAsync(string modelOutput, string? sourceConversationId = null);

    /// <summary>
    /// Remove memory markers from model output (clean display text for UI).
    /// Returns text with [MEMORY: ...] blocks removed.
    /// </summary>
    string CleanMemoryMarkers(string modelOutput);
}
