using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class ModelProfileService
{
    private readonly ISettingsService _settings;

    public ModelProfileService(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<ModelProfile> Profiles => _settings.Settings.ModelProfiles;

    public ModelProfile GetOrCreate(string modelId, string backend = "")
    {
        var profile = Get(modelId);
        if (profile is not null) return profile;

        profile = new ModelProfile
        {
            ModelId = modelId,
            Backend = backend,
            IsVisible = true
        };
        _settings.Settings.ModelProfiles.Add(profile);
        return profile;
    }

    public ModelProfile? Get(string modelId) =>
        _settings.Settings.ModelProfiles.FirstOrDefault(p =>
            string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

    public void ApplyProfiles(IList<LlmModel> models)
    {
        foreach (var model in models)
        {
            var profile = Get(model.Id);
            if (profile is not null)
            {
                model.ProfileDisplayName = profile.DisplayName;
                model.Description = profile.Description;
                model.Tags = NormalizeTags(profile.Tags);
                model.DefaultTemperature = profile.DefaultTemperature;
                model.DefaultContextSize = profile.DefaultContextSize;
                model.DefaultMaxTokens = profile.DefaultMaxTokens;
                model.DefaultTopP = profile.DefaultTopP;
                model.DefaultTopK = profile.DefaultTopK;
                model.DefaultMinP = profile.DefaultMinP;
                model.DefaultRepeatPenalty = profile.DefaultRepeatPenalty;
                model.DefaultFrequencyPenalty = profile.DefaultFrequencyPenalty;
                model.DefaultPresencePenalty = profile.DefaultPresencePenalty;
                model.IsVisible = profile.IsVisible;
                model.Avatar = profile.Avatar;
            }

            // No explicit user override: fall back to the live-probed context
            // length instead of leaving budget math to guess.
            model.DefaultContextSize ??= model.ProbedContextLength;
        }
    }

    public async Task SaveAsync(ModelProfile profile, CancellationToken ct = default)
    {
        var normalized = Normalize(profile);
        var existing = Get(normalized.ModelId);
        if (existing is null)
        {
            _settings.Settings.ModelProfiles.Add(normalized);
        }
        else
        {
            existing.DisplayName = normalized.DisplayName;
            existing.Description = normalized.Description;
            existing.Tags = normalized.Tags;
            existing.DefaultTemperature = normalized.DefaultTemperature;
            existing.DefaultContextSize = normalized.DefaultContextSize;
            existing.DefaultMaxTokens = normalized.DefaultMaxTokens;
            existing.DefaultTopP = normalized.DefaultTopP;
            existing.DefaultTopK = normalized.DefaultTopK;
            existing.DefaultMinP = normalized.DefaultMinP;
            existing.DefaultRepeatPenalty = normalized.DefaultRepeatPenalty;
            existing.DefaultFrequencyPenalty = normalized.DefaultFrequencyPenalty;
            existing.DefaultPresencePenalty = normalized.DefaultPresencePenalty;
            existing.Backend = normalized.Backend;
            existing.IsVisible = normalized.IsVisible;
            existing.Avatar = normalized.Avatar;
        }

        await _settings.SaveAsync();
    }

    public async Task ResetAsync(string modelId, CancellationToken ct = default)
    {
        _settings.Settings.ModelProfiles.RemoveAll(p =>
            string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        await _settings.SaveAsync();
    }

    private static ModelProfile Normalize(ModelProfile profile) => new()
    {
        ModelId = profile.ModelId.Trim(),
        DisplayName = profile.DisplayName.Trim(),
        Description = profile.Description.Trim(),
        Tags = NormalizeTags(profile.Tags),
        DefaultTemperature = profile.DefaultTemperature,
        DefaultContextSize = profile.DefaultContextSize,
        DefaultMaxTokens = profile.DefaultMaxTokens,
        DefaultTopP = profile.DefaultTopP,
        DefaultTopK = profile.DefaultTopK,
        DefaultMinP = profile.DefaultMinP,
        DefaultRepeatPenalty = profile.DefaultRepeatPenalty,
        DefaultFrequencyPenalty = profile.DefaultFrequencyPenalty,
        DefaultPresencePenalty = profile.DefaultPresencePenalty,
        Backend = profile.Backend.Trim(),
        IsVisible = profile.IsVisible,
        Avatar = profile.Avatar.Trim()
    };

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
