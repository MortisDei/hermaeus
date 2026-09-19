using Hermaeus.Core.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Resolves a planner or persisted model reference against the current
/// eligible inventory. Stable ids are authoritative. Display labels are
/// accepted only as a compatibility bridge for legacy task state and model
/// responses, and an ambiguous label is never guessed.
/// </summary>
internal static class AgentModelIdentityResolver
{
    public static LlmModel? Resolve(IReadOnlyList<LlmModel> models, string? requested)
    {
        var value = requested?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return null;

        var stable = models.Where(model => string.Equals(model.Id, value, StringComparison.Ordinal)).ToArray();
        if (stable.Length == 1)
            return stable[0];

        var normalized = Normalize(value);
        var matches = models.Where(model => MatchesDisplayReference(model, normalized)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool MatchesDisplayReference(LlmModel model, string normalized)
    {
        var labels = new[]
        {
            model.DisplayName,
            model.Name,
            model.ProfileDisplayName,
            WithProvider(model.Name, model.Provider),
            WithProvider(model.ProfileDisplayName, model.Provider),
            WithProvider(model.Name, model.ProviderTag),
            WithProvider(model.ProfileDisplayName, model.ProviderTag)
        };
        return labels.Any(label => label.Length > 0 && string.Equals(Normalize(label), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string WithProvider(string name, string provider) =>
        string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(provider)
            ? string.Empty
            : $"{name.Trim()} [{provider.Trim()}]";

    private static string Normalize(string value) => string.Join(
        ' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
