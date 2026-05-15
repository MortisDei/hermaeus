using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Service for injecting stored memories into chat context and system prompts.
/// </summary>
public interface IMemoryInjectionService
{
    /// <summary>
    /// Build a prompt section summarizing available memories for injection.
    /// </summary>
    /// <param name="memories">List of memories to include</param>
    /// <returns>Formatted prompt text describing the memories</returns>
    string BuildMemoryContext(List<Memory> memories);

    /// <summary>
    /// Generate system prompt instructions that teach the model how to emit memories.
    /// Includes examples of [MEMORY: ...] format.
    /// </summary>
    /// <returns>System prompt instruction text</returns>
    string GetMemoryInstructionPrompt();

    /// <summary>
    /// Select the most important memories to inject given a token budget.
    /// Uses importance score and recency to rank memories.
    /// </summary>
    /// <param name="memories">Available memories to choose from</param>
    /// <param name="tokenBudget">Approximate token limit for memory injection</param>
    /// <returns>Selected subset of memories to inject</returns>
    Task<List<Memory>> SelectMemoriesForInjectionAsync(List<Memory> memories, int tokenBudget = 500);
}
