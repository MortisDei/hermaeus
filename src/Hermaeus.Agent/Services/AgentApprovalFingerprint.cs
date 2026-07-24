using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Binds a pending tool action's identity to what the approval UI actually
/// rendered (r23 4.1). A SHA256 hash over the tool name and a canonical
/// (sorted-key, compact) serialization of its arguments, so the same
/// tool/arguments pair always fingerprints the same way regardless of
/// dictionary enumeration order.
/// </summary>
public static class AgentApprovalFingerprint
{
    private static readonly JsonSerializerOptions CanonicalOptions = new() { WriteIndented = false };

    public static string Compute(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        var sortedArguments = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in arguments)
            sortedArguments[pair.Key] = pair.Value;

        var canonicalArguments = JsonSerializer.Serialize(sortedArguments, CanonicalOptions);
        var payload = $"{toolName}\n{canonicalArguments}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The fingerprint to treat as "current" for a pending action: its own
    /// stored value when present, else freshly computed from ToolName and
    /// Arguments (covers a pre-r23 persisted task with no stored
    /// Fingerprint). Empty when there is no pending action at all.
    /// </summary>
    public static string Resolve(AgentPendingToolAction? pending) =>
        pending is null
            ? string.Empty
            : pending.Fingerprint is { Length: > 0 } ? pending.Fingerprint : Compute(pending.ToolName, pending.Arguments);
}
